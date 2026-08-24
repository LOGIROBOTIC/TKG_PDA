using TkgPda.Domain;

namespace TkgPda.Application;

public sealed record PdaActorContext(
    Guid UserId,
    IReadOnlySet<string> Roles,
    bool IsDeviceBound);

public sealed class PdaOperationalContextService(IApiDataStore store)
{
    public async Task<OperationalContextRecord?> GetAsync(
        PdaActorContext actor,
        Guid machineId,
        bool administrator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var data = await store.GetOperationalContextDataAsync(
            actor.UserId,
            machineId,
            administrator,
            cancellationToken);
        return data is null ? null : PdaOperationalContextPolicy.Project(data, actor);
    }
}

public static class PdaOperationalContextPolicy
{
    private static readonly TransportTaskType[] SupportedActions =
    [
        TransportTaskType.BringEmpty,
        TransportTaskType.RemoveFull,
        TransportTaskType.RemoveEmpty
    ];

    public static OperationalContextRecord Project(OperationalContextDataRecord data, PdaActorContext actor)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(actor);

        var busySlots = data.ActiveTransportRequests
            .SelectMany(static request => new[] { request.SourceSlotId, request.DestinationSlotId })
            .Where(static slotId => slotId.HasValue)
            .Select(static slotId => slotId!.Value)
            .ToHashSet();

        var slots = data.Slots
            .Select(slot => ProjectSlot(slot, actor, data.Machine.Permissions.CanOperate, busySlots.Contains(slot.Id)))
            .ToArray();
        var technicalAuthorized = actor.IsDeviceBound
            && (actor.Roles.Contains(KnownRoleCodes.Admin) || actor.Roles.Contains(KnownRoleCodes.Supervisor));
        var requests = data.ActiveTransportRequests
            .Select(request => ProjectRequest(request, actor, data.Machine.Permissions.CanOperate, technicalAuthorized))
            .ToArray();

        return new OperationalContextRecord(
            data.Machine,
            data.ActiveVariantId,
            data.ActiveVariantCode,
            data.ActiveVariantName,
            data.ActiveLayoutId,
            data.ActiveLayoutCode,
            data.ActiveLayoutName,
            data.ActiveNavigationProfileId,
            data.ActiveNavigationProfileCode,
            data.RuntimeRowVersion,
            data.RuntimeUpdatedAt,
            data.VariantLayouts,
            slots,
            data.ProductGroups,
            requests,
            data.TaskTemplates);
    }

    public static IReadOnlyList<PdaAvailableActionRecord> EvaluateActions(
        SlotContextDataRecord slot,
        PdaActorContext actor,
        bool canOperate,
        bool hasActiveRequest)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(actor);
        return SupportedActions
            .Select(action => EvaluateAction(slot, actor, canOperate, hasActiveRequest, action))
            .ToArray();
    }

    private static SlotContextRecord ProjectSlot(
        SlotContextDataRecord slot,
        PdaActorContext actor,
        bool canOperate,
        bool hasActiveRequest)
    {
        var readiness = ExecutionReadiness(slot.RdsWorkSite);
        return new SlotContextRecord(
            slot.Id,
            slot.Code,
            slot.DisplayName,
            slot.Role,
            slot.IsEnabled,
            slot.TransportEligible,
            slot.DefaultContainerTypeId,
            slot.DefaultContainerCode,
            slot.X,
            slot.Y,
            slot.Width,
            slot.Height,
            slot.DisplayOrder,
            slot.OccupancyState,
            slot.LoadState,
            slot.CurrentPartId,
            slot.CurrentPartCode,
            slot.CurrentContainerTypeId,
            slot.CurrentContainerCode,
            slot.RowVersion,
            slot.RuntimeUpdatedAt,
            slot.RdsWorkSite,
            readiness,
            EvaluateActions(slot, actor, canOperate, hasActiveRequest));
    }

    private static PdaExecutionReadinessRecord ExecutionReadiness(RdsWorkSiteProjectionRecord workSite)
    {
        if (!workSite.IsMapped)
        {
            return new(false, false, "RDS_WORKSITE_NOT_MAPPED", "Bu lojistik konumu henüz bir RDS WorkSite ile eşleştirilmemiştir.");
        }

        if (workSite.Type != 1)
        {
            return new(true, false, "RDS_WORKSITE_NOT_PHYSICAL", "Eşlenen RDS WorkSite fiziksel bir taşıma noktası değildir.");
        }

        if (!workSite.LastSyncAt.HasValue)
        {
            return new(true, false, "RDS_WORKSITE_STALE", "Eşlenen RDS WorkSite için başarılı senkronizasyon kanıtı yoktur.");
        }

        if (workSite.Disabled == 1)
        {
            return new(true, false, "RDS_WORKSITE_DISABLED", "Eşlenen RDS WorkSite devre dışıdır.");
        }

        if (workSite.SyncFailed == 1)
        {
            return new(true, false, "RDS_WORKSITE_SYNC_FAILED", "RDS WorkSite senkronizasyon hatası bildiriyor.");
        }

        if (workSite.Locked == 1)
        {
            return new(true, false, "RDS_WORKSITE_LOCKED", "RDS WorkSite şu anda kilitli veya meşguldür.");
        }

        return new(true, true, null, null);
    }

    private static PdaAvailableActionRecord EvaluateAction(
        SlotContextDataRecord slot,
        PdaActorContext actor,
        bool canOperate,
        bool hasActiveRequest,
        TransportTaskType action)
    {
        var operation = action.ToDatabase();
        if (!actor.IsDeviceBound)
        {
            return Disabled(operation, "PDA_DEVICE_REQUIRED", "Bu işlem yalnız cihaz bağlı PDA oturumunda kullanılabilir.");
        }

        var canRemove = actor.Roles.Contains(KnownRoleCodes.Admin)
            || actor.Roles.Contains(KnownRoleCodes.Supervisor)
            || actor.Roles.Contains(KnownRoleCodes.LineOperator);
        var canBring = canRemove || actor.Roles.Contains(KnownRoleCodes.WarehouseOperator);
        if ((action == TransportTaskType.BringEmpty && !canBring)
            || (action != TransportTaskType.BringEmpty && !canRemove))
        {
            return Disabled(operation, "OPERATION_NOT_ALLOWED_FOR_ROLE", "Rolünüz bu lojistik işlemini başlatmaya yetkili değildir.");
        }

        if (!canOperate)
        {
            return Disabled(operation, "MACHINE_OPERATION_NOT_PERMITTED", "Bu makinede operasyon yetkiniz bulunmuyor.");
        }

        if (slot.Role == "MACHINE")
        {
            return Disabled(operation, "MACHINE_REGION_FORBIDDEN", "Makine/hücre bölgeleri yalnız ekran referansıdır ve taşıma noktası değildir.");
        }

        if (!slot.IsEnabled)
        {
            return Disabled(operation, "SLOT_DISABLED", "Bu lojistik konumu aktif değildir.");
        }

        if (hasActiveRequest)
        {
            return Disabled(operation, "SLOT_BUSY", "Bu lojistik konumunu kullanan aktif bir taşıma talebi bulunuyor.");
        }

        var stateMatches = action switch
        {
            TransportTaskType.BringEmpty => slot.OccupancyState == "VACANT" && slot.LoadState == "UNKNOWN",
            TransportTaskType.RemoveFull => slot.OccupancyState == "OCCUPIED" && slot.LoadState == "FULL",
            TransportTaskType.RemoveEmpty => slot.OccupancyState == "OCCUPIED" && slot.LoadState == "EMPTY",
            _ => false
        };
        return stateMatches
            ? new PdaAvailableActionRecord(operation, true, null, null)
            : Disabled(operation, "SLOT_STATE_MISMATCH", "Konumun mevcut doluluk/yük durumu bu işlemle uyumlu değildir.");
    }

    private static ActiveTransportRequestRecord ProjectRequest(
        ActiveTransportDataRecord request,
        PdaActorContext actor,
        bool canOperate,
        bool technicalAuthorized)
    {
        var cancellation = EvaluateCancellation(request, actor, canOperate);
        return new ActiveTransportRequestRecord(
            request.RequestId,
            request.RequestNo,
            request.Operation.ToDatabase(),
            request.Status.ToDatabase(),
            request.RequestedBy,
            request.RequestedByUsername,
            request.IsMine,
            request.SourceSlotId,
            request.DestinationSlotId,
            request.ProductGroupId,
            request.PartId,
            request.ContainerTypeId,
            request.Priority,
            request.CreatedAt,
            cancellation.Enabled,
            cancellation.DisabledCode,
            cancellation.DisabledMessage,
            PdaExecutionProjector.Project(request, technicalAuthorized));
    }

    private static PdaAvailableActionRecord EvaluateCancellation(
        ActiveTransportDataRecord request,
        PdaActorContext actor,
        bool canOperate)
    {
        if (!actor.IsDeviceBound)
        {
            return Disabled("CANCEL", "PDA_DEVICE_REQUIRED", "İptal işlemi yalnız cihaz bağlı PDA oturumunda kullanılabilir.");
        }

        if (!canOperate)
        {
            return Disabled("CANCEL", "MACHINE_OPERATION_NOT_PERMITTED", "Bu makinede operasyon yetkiniz bulunmuyor.");
        }

        var elevated = actor.Roles.Contains(KnownRoleCodes.Admin)
            || actor.Roles.Contains(KnownRoleCodes.Supervisor)
            || actor.Roles.Contains(KnownRoleCodes.LineOperator);
        var warehouseOwnBring = actor.Roles.Contains(KnownRoleCodes.WarehouseOperator)
            && request.Operation == TransportTaskType.BringEmpty
            && request.IsMine;
        if (!elevated && !warehouseOwnBring)
        {
            return Disabled("CANCEL", "CANCELLATION_NOT_ALLOWED_FOR_ROLE", "Bu talebi iptal etme yetkiniz bulunmuyor.");
        }

        return TransportStateMachine.CanCancelBeforeExecution(request.Status)
            ? new PdaAvailableActionRecord("CANCEL", true, null, null)
            : Disabled(
                "CANCEL",
                WorkflowErrorCodes.CancellationAfterExecutionDeferred,
                "RDS yürütme sınırına ulaşmış bir talep PDA üzerinden iptal edilemez.");
    }

    private static PdaAvailableActionRecord Disabled(string action, string code, string message) =>
        new(action, false, code, message);
}

public static class PdaExecutionProjector
{
    public static PdaExecutionProjectionRecord Project(ActiveTransportDataRecord request, bool technicalAuthorized)
    {
        ArgumentNullException.ThrowIfNull(request);
        var failure = Failure(request);
        return new PdaExecutionProjectionRecord(
            State(request),
            request.RdsTask?.TaskRecordId,
            request.RdsTask?.TaskLabel,
            request.RdsTask?.OutOrderNo,
            request.RdsTask?.AgvId,
            request.RdsTask?.Status,
            request.RdsTask?.LastSyncAt,
            failure.Code,
            failure.Category,
            failure.OperatorMessage,
            technicalAuthorized ? request.FailureMessage ?? request.RdsTask?.EndedReason : null,
            request.CorrelationId);
    }

    private static string State(ActiveTransportDataRecord request)
    {
        // TKG request status is the business source of truth once it is terminal.
        // Persisted RDS status refines only requests that remain active in TKG.
        if (request.Status == TransportRequestStatus.Completed)
        {
            return "COMPLETED";
        }

        if (request.Status == TransportRequestStatus.Failed)
        {
            return "FAILED";
        }

        if (request.Status == TransportRequestStatus.Cancelled)
        {
            return "TERMINATED";
        }

        if (request.RdsTask?.Status == 1003)
        {
            return "COMPLETED";
        }

        if (request.RdsTask?.Status is 1004 or 1005 or 1006)
        {
            return "FAILED";
        }

        if (request.RdsTask?.Status == 1001)
        {
            return "TERMINATED";
        }

        if (IsTrafficWaiting(request.FailureCode) || request.RdsTask?.Status == 1002)
        {
            return "WAITING";
        }

        return request.Status switch
        {
            TransportRequestStatus.Created or TransportRequestStatus.Validating => "REQUESTED",
            TransportRequestStatus.Queued => "QUEUED",
            TransportRequestStatus.SentToRds when request.RdsTask?.Status == 1000 && request.RdsTask.StartedAt.HasValue => "RUNNING",
            TransportRequestStatus.SentToRds when request.RdsTask?.Status == 1000 && !string.IsNullOrWhiteSpace(request.RdsTask.AgvId) => "ROBOT_ASSIGNED",
            TransportRequestStatus.SentToRds => "WAITING_FOR_ROBOT",
            TransportRequestStatus.Running => "RUNNING",
            _ => "REQUESTED"
        };
    }

    private static (string? Code, string? Category, string? OperatorMessage) Failure(ActiveTransportDataRecord request)
    {
        var code = request.FailureCode?.Trim();
        var normalized = code?.ToUpperInvariant();
        if (normalized is "60008" or "FAIL_LOCK_BIN")
        {
            return ("WORKSITE_BUSY", "WORKSITE", "Çalışma alanı başka bir görev tarafından kullanılıyor.");
        }

        if (normalized is "60009" or "NO_PATH")
        {
            return ("PATH_UNAVAILABLE", "NAVIGATION", "Robot için güvenli bir ulaşım yolu bulunamadı.");
        }

        if (normalized is "60011" or "TASK_FAILED")
        {
            return ("EXECUTION_FAILED", "EXECUTION", "Robot görevi tamamlanamadı. Yetkili desteğe başvurun.");
        }

        if (IsTrafficWaiting(normalized))
        {
            return ("TRAFFIC_WAITING", "TRAFFIC", "Robot trafiği nedeniyle görev bekliyor.");
        }

        if (request.Status == TransportRequestStatus.Failed || request.RdsTask?.Status is 1004 or 1005 or 1006)
        {
            return ("EXECUTION_FAILED", "EXECUTION", "Robot görevi tamamlanamadı. Yetkili desteğe başvurun.");
        }

        return (null, null, null);
    }

    private static bool IsTrafficWaiting(string? failureCode) =>
        failureCode?.Trim().ToUpperInvariant() is "54002" or "54003";
}
