using System.Text.Json;
using TkgPda.Domain;

namespace TkgPda.Application;

public sealed record RdsExecutionSettings(
    bool DispatchEnabled,
    TimeSpan WorkSiteFreshness,
    string? EmptyContentValue,
    string? FullContentValue);

public sealed record RdsDispatchEndpoint(
    Guid? SlotId,
    string? SlotCode,
    string? SlotGroupCode,
    string? SlotRole,
    string? SiteId,
    int? Type,
    int? Filled,
    int? Locked,
    int? Disabled,
    int? SyncFailed,
    DateTimeOffset? LastSyncAt,
    bool IsMapped);

public sealed record RdsDispatchContext(
    TransportRequestSnapshot Request,
    string MachineCode,
    string VariantCode,
    string? ProductGroupCode,
    string? PartCode,
    string? ContainerCode,
    Guid TaskTemplateId,
    string? RdsTaskLabel,
    int TemplatePriority,
    JsonElement TemplateConfig,
    RdsDispatchEndpoint Source,
    RdsDispatchEndpoint Destination);

public sealed record RdsTaskProjection(
    Guid TransportRequestId,
    string TaskRecordId,
    string? TaskLabel,
    int? Status,
    string? AgvId,
    string? OutOrderNo,
    string? EndedReason,
    JsonElement RequestPayload,
    JsonElement ResponsePayload,
    JsonElement LastStatus,
    bool CancellationRequested,
    TransportRequestStatus RequestStatus);

public sealed record RdsTaskTemplateSummary(
    Guid Id,
    Guid VariantId,
    string VariantCode,
    Guid? ProductGroupId,
    string? ProductGroupCode,
    string Operation,
    string? RdsTaskLabel,
    int Priority,
    JsonElement Config,
    bool IsActive);

public sealed record RdsMappedWorkSite(
    Guid SlotId,
    Guid WorkSiteId,
    string SiteId,
    int? Type,
    int? Disabled,
    int? SyncFailed,
    DateTimeOffset? LastSyncAt);

public sealed record RdsNavigationProfileSummary(
    Guid Id,
    string Code,
    string Name,
    string Status,
    string? RobotMapName,
    string? SwitchTransitionStation,
    string? Vehicle,
    JsonElement ConfigJson);

public interface IRdsExecutionLease : IAsyncDisposable;

public interface IRdsExecutionStore
{
    Task<IReadOnlyList<Guid>> ListQueuedRequestIdsAsync(int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RdsTaskProjection>> ListActiveTasksAsync(int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> ListPendingWorkSiteRefreshRequestIdsAsync(int limit, CancellationToken cancellationToken = default);
    Task<IRdsExecutionLease?> TryAcquireRequestLeaseAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<RdsDispatchContext?> GetDispatchContextAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<TransportRequestSnapshot?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<RdsTaskProjection?> FindTaskByRequestAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<RdsTaskProjection?> FindTaskByRecordIdAsync(string taskRecordId, CancellationToken cancellationToken = default);
    Task EnsureTaskRecordAsync(Guid requestId, string taskRecordId, string taskLabel, JsonElement requestPayload, CancellationToken cancellationToken = default);
    Task RecordDispatchResponseAsync(Guid requestId, JsonElement responsePayload, DateTimeOffset syncedAt, CancellationToken cancellationToken = default);
    Task RecordTaskStatusAsync(Guid requestId, RdsWindTaskSnapshot task, DateTimeOffset syncedAt, CancellationToken cancellationToken = default);
    Task MarkCancellationRequestedAsync(Guid requestId, JsonElement responsePayload, DateTimeOffset requestedAt, CancellationToken cancellationToken = default);
    Task WriteExecutionAuditAsync(Guid requestId, string action, string code, string message, Guid correlationId, CancellationToken cancellationToken = default);
    Task<RdsMappedWorkSite?> FindMappedWorkSiteAsync(Guid slotId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RdsTaskTemplateSummary>> ListTaskTemplatesAsync(CancellationToken cancellationToken = default);
    Task EnrichRemainingTimesAsync(IReadOnlyDictionary<string, double> agvRemainingTimes, CancellationToken cancellationToken = default);
    Task<RdsNavigationProfileSummary?> GetNavigationProfileSummaryAsync(Guid? profileId, Guid variantId, Guid machineId, CancellationToken cancellationToken = default);
    Task<bool> HasUnreleasedMapSwitchLockAsync(Guid requestId, string vehicle, CancellationToken cancellationToken = default);
}

public sealed record RdsValidateTransportRequest(
    string TaskRecordId,
    string TaskLabel,
    IReadOnlyDictionary<string, object?> InputParams);

public interface IRobotTelemetryProvider
{
    Task<IReadOnlyDictionary<string, double>> GetRobotRemainingTimesAsync(CancellationToken cancellationToken = default);
}

public sealed record RdsValidationResult(
    bool Validated,
    bool TaskLabelExists,
    bool TaskRecordExists,
    int Code,
    string Message,
    JsonElement RawResponse);

public sealed record RdsCreateTransportRequest(
    string TaskRecordId,
    string TaskLabel,
    int Priority,
    IReadOnlyDictionary<string, object?> InputParams);

public sealed record RdsCreateTransportResult(
    bool Accepted,
    bool IdempotentReplay,
    int Code,
    string Message,
    JsonElement RawResponse);

public sealed record RdsStopTaskResult(int Code, string Message, JsonElement RawResponse);

public sealed record RdsContentUpdateResult(int Code, string Message, JsonElement RawResponse);

public interface IRdsExecutionClient
{
    bool DispatchEnabled { get; }
    Task<RdsValidationResult> ValidateTransportAsync(RdsValidateTransportRequest request, Guid correlationId, CancellationToken cancellationToken = default);
    Task<RdsCreateTransportResult> CreateTransportAsync(RdsCreateTransportRequest request, Guid correlationId, CancellationToken cancellationToken = default);
    Task<RdsStopTaskResult> StopTaskAsync(string taskRecordId, Guid correlationId, CancellationToken cancellationToken = default);
    Task<RdsContentUpdateResult> UpdateContentAsync(string siteId, string content, Guid correlationId, CancellationToken cancellationToken = default);
}

public sealed class RdsExecutionException(string code, string message, Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public string Code { get; } = code;
}

public sealed record RdsDispatchAttemptResult(
    Guid RequestId,
    string TaskRecordId,
    bool Dispatched,
    bool IdempotentReplay,
    string Code,
    string Message);

public sealed record RdsCancellationResult(
    TransportRequestSnapshot Request,
    bool PendingRdsConfirmation,
    bool WasIdempotentReplay);

public sealed record RdsLoadContentSyncResult(
    string Status,
    string? SiteId,
    string? ConfiguredContentValue,
    string? Message);

public sealed record RdsCallbackEvent(string EventType, JsonElement RawPayload, DateTimeOffset? EventTimestamp);

public sealed record RdsCallbackResult(bool Accepted, bool Mutated, string Code, string Message, string? TaskRecordId);

public static class RdsTaskInputBinder
{
    private static readonly HashSet<string> SupportedSources = new(StringComparer.Ordinal)
    {
        "SOURCE_RDS_SITE_ID",
        "DESTINATION_RDS_SITE_ID",
        "SOURCE_SLOT_GROUP_CODE",
        "DESTINATION_SLOT_GROUP_CODE",
        "MACHINE_CODE",
        "VARIANT_CODE",
        "PRODUCT_GROUP_CODE",
        "PART_CODE",
        "CONTAINER_CODE",
        "REQUEST_ID",
        "REQUEST_NO",
        "TASK_TYPE",
        "LITERAL"
    };

    public static IReadOnlyDictionary<string, object?> Bind(RdsDispatchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.TemplateConfig.ValueKind != JsonValueKind.Object
            || !context.TemplateConfig.TryGetProperty("rdsInputBindings", out var bindings)
            || bindings.ValueKind != JsonValueKind.Array)
        {
            throw new RdsExecutionException("RDS_TASK_BINDINGS_REQUIRED", "The task template must define an rdsInputBindings array.");
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var binding in bindings.EnumerateArray())
        {
            if (binding.ValueKind != JsonValueKind.Object)
            {
                throw new RdsExecutionException("RDS_TASK_BINDING_INVALID", "Every RDS input binding must be an object.");
            }

            var param = Text(binding, "param");
            var source = Text(binding, "source");
            var required = binding.TryGetProperty("required", out var requiredElement)
                && requiredElement.ValueKind == JsonValueKind.True;
            if (string.IsNullOrWhiteSpace(param) || param.Length > 200 || string.IsNullOrWhiteSpace(source) || !SupportedSources.Contains(source))
            {
                throw new RdsExecutionException("RDS_TASK_BINDING_INVALID", "An RDS input binding contains an invalid param or unsupported source.");
            }

            if (!values.TryAdd(param, Resolve(context, binding, source)))
            {
                throw new RdsExecutionException("RDS_TASK_BINDING_DUPLICATE", $"RDS input parameter '{param}' is configured more than once.");
            }

            if (required && IsMissing(values[param]))
            {
                throw new RdsExecutionException("RDS_TASK_INPUT_UNRESOLVED", $"Required RDS input '{param}' could not be resolved from {source}.");
            }
        }

        if (values.Count == 0)
        {
            throw new RdsExecutionException("RDS_TASK_BINDINGS_REQUIRED", "At least one explicit RDS input binding is required.");
        }

        return values;
    }

    private static object? Resolve(RdsDispatchContext context, JsonElement binding, string source) => source switch
    {
        "SOURCE_RDS_SITE_ID" => context.Source.SiteId,
        "DESTINATION_RDS_SITE_ID" => context.Destination.SiteId,
        "SOURCE_SLOT_GROUP_CODE" => context.Source.SlotGroupCode,
        "DESTINATION_SLOT_GROUP_CODE" => context.Destination.SlotGroupCode,
        "MACHINE_CODE" => context.MachineCode,
        "VARIANT_CODE" => context.VariantCode,
        "PRODUCT_GROUP_CODE" => context.ProductGroupCode,
        "PART_CODE" => context.PartCode,
        "CONTAINER_CODE" => context.ContainerCode,
        "REQUEST_ID" => context.Request.Id.ToString(),
        "REQUEST_NO" => context.Request.RequestNo,
        "TASK_TYPE" => context.Request.TaskType.ToDatabase(),
        "LITERAL" => binding.TryGetProperty("value", out var literal) ? JsonLiteral(literal) : null,
        _ => null
    };

    private static object? JsonLiteral(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new RdsExecutionException("RDS_TASK_BINDING_INVALID", "LITERAL supports only scalar JSON values.")
    };

    private static bool IsMissing(object? value) => value is null || value is string text && string.IsNullOrWhiteSpace(text);

    private static string? Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim()
            : null;
}

public sealed class RdsExecutionCoordinator(
    IRdsExecutionStore store,
    IRdsExecutionClient executionClient,
    IRdsReadClient readClient,
    IRdsWorkSiteReconciler workSiteReconciler,
    TransportWorkflowService workflow,
    RdsExecutionSettings settings,
    TimeProvider timeProvider,
    IRobotTelemetryProvider? robotTelemetry = null,
    IRdsCoreNativeOrderClient? nativeOrderClient = null)
{
    private bool IsNativeMode => nativeOrderClient is not null
        && nativeOrderClient.Mode == RdsExecutionMode.RdsCoreNativeOrder
        && nativeOrderClient.MutationEnabled;

    public bool IsDispatchEnabled => (settings.DispatchEnabled && executionClient.DispatchEnabled) || IsNativeMode;

    public async Task<IReadOnlyList<RdsDispatchAttemptResult>> DispatchQueuedAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (!IsDispatchEnabled)
        {
            return [];
        }

        var ids = await store.ListQueuedRequestIdsAsync(Math.Clamp(limit, 1, 100), cancellationToken);
        var results = new List<RdsDispatchAttemptResult>();
        foreach (var id in ids)
        {
            var correlationId = Guid.NewGuid();
            try
            {
                var result = await DispatchOneAsync(id, correlationId, cancellationToken);
                if (result is not null)
                {
                    results.Add(result);
                }
            }
            catch (RdsExecutionException error)
            {
                await store.WriteExecutionAuditAsync(id, "RDS_DISPATCH_BLOCKED", error.Code, error.Message, correlationId, cancellationToken);
                results.Add(new(id, id.ToString(), false, false, error.Code, error.Message));
            }
            catch (RdsReadException error)
            {
                await store.WriteExecutionAuditAsync(id, "RDS_DISPATCH_DEFERRED", error.Code, error.Message, correlationId, cancellationToken);
                results.Add(new(id, id.ToString(), false, false, error.Code, "RDS read evidence is unavailable; the request remains queued."));
            }
            catch (RdsCoreNativeOrderException error)
            {
                await store.WriteExecutionAuditAsync(id, "RDS_DISPATCH_DEFERRED", error.Code, error.Message, correlationId, cancellationToken);
                results.Add(new(id, id.ToString(), false, false, error.Code, "RDSCore is temporarily unavailable; the request remains queued."));
            }
        }

        return results;
    }

    public async Task<RdsDispatchAttemptResult?> DispatchOneAsync(Guid requestId, Guid correlationId, CancellationToken cancellationToken = default)
    {
        await using var lease = await store.TryAcquireRequestLeaseAsync(requestId, cancellationToken);
        if (lease is null)
        {
            return null;
        }

        var context = await store.GetDispatchContextAsync(requestId, cancellationToken)
            ?? throw new RdsExecutionException("RDS_DISPATCH_CONTEXT_NOT_FOUND", "The queued transport request no longer has a dispatch context.");
        if (context.Request.Status != TransportRequestStatus.Queued)
        {
            return new(requestId, requestId.ToString(), false, false, "REQUEST_NOT_QUEUED", "The request is no longer queued.");
        }

        if (context.Request.ReadinessState == TransportReadinessState.WaitingPreparation)
        {
            return new(requestId, requestId.ToString(), false, false, "READINESS_WAITING_PREPARATION", "The request is waiting for warehouse preparation.");
        }

        var executionModeStr = GetLocation(context.TemplateConfig, "executionMode");
        if (string.IsNullOrWhiteSpace(executionModeStr))
        {
            await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_BLOCKED", "EXECUTION_MODE_UNRESOLVED", "The task template configuration does not define an explicit executionMode.", correlationId, cancellationToken);
            return new(requestId, requestId.ToString(), false, false, "EXECUTION_MODE_UNRESOLVED", "The task template configuration does not define an explicit executionMode.");
        }

        var isNative = string.Equals(executionModeStr, "RDSCORE_NATIVE_ORDER", StringComparison.OrdinalIgnoreCase);
        var isWindTask = string.Equals(executionModeStr, "WIND_TASK", StringComparison.OrdinalIgnoreCase);

        if (!isNative && !isWindTask)
        {
            await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_BLOCKED", "EXECUTION_MODE_INVALID", $"Unsupported executionMode '{executionModeStr}'. Must be RDSCORE_NATIVE_ORDER or WIND_TASK.", correlationId, cancellationToken);
            return new(requestId, requestId.ToString(), false, false, "EXECUTION_MODE_INVALID", $"Unsupported executionMode '{executionModeStr}'. Must be RDSCORE_NATIVE_ORDER or WIND_TASK.");
        }

        if (isNative)
        {
            if (!IsNativeMode)
            {
                await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_BLOCKED", "NATIVE_DISPATCH_DISABLED", "Template requires RDSCORE_NATIVE_ORDER but Native dispatch is not configured or disabled.", correlationId, cancellationToken);
                return new(requestId, requestId.ToString(), false, false, "NATIVE_DISPATCH_DISABLED", "Template requires RDSCORE_NATIVE_ORDER but Native dispatch is not configured or disabled.");
            }
            return await DispatchNativeOrderAsync(requestId, context, correlationId, cancellationToken);
        }

        var taskLabel = context.RdsTaskLabel?.Trim();
        if (string.IsNullOrWhiteSpace(taskLabel))
        {
            return new(requestId, requestId.ToString(), false, false, "RDS_TASK_TEMPLATE_NOT_READY", "The matching task template has no RDS task label.");
        }

        var inputParams = RdsTaskInputBinder.Bind(context);
        ValidateReferencedEndpoints(context, inputParams);
        var taskRecordId = requestId.ToString();
        var createRequest = new RdsCreateTransportRequest(taskRecordId, taskLabel, context.TemplatePriority, inputParams);
        var requestPayload = JsonSerializer.SerializeToElement(createRequest);
        await store.EnsureTaskRecordAsync(requestId, taskRecordId, taskLabel, requestPayload, cancellationToken);
        await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_ATTEMPTED", "RDS_CREATE_REQUEST", "Dispatch was attempted with the immutable TKG request identity.", correlationId, cancellationToken);

        RdsCreateTransportResult? createResult = null;
        try
        {
            createResult = await executionClient.CreateTransportAsync(createRequest, correlationId, cancellationToken);
            await store.RecordDispatchResponseAsync(requestId, createResult.RawResponse, timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (RdsExecutionException)
        {
            // A lost HTTP response is ambiguous. Query the same taskRecordId before deciding whether to retry.
        }

        var query = await readClient.QueryWindTasksAsync(new(1, 100, TaskRecordId: taskRecordId), correlationId, cancellationToken);
        var exact = query.Data.Tasks.Where(task => string.Equals(task.TaskRecordId, taskRecordId, StringComparison.Ordinal)).ToArray();
        if (exact.Length > 1 || query.Data.TotalCount > 1)
        {
            throw new RdsExecutionException("RDS_DUPLICATE_TASK_IDENTITY", "RDS returned more than one task for the immutable TKG taskRecordId.");
        }

        if (exact.Length == 0)
        {
            await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_UNCONFIRMED", "RDS_TASK_NOT_YET_OBSERVED", "The create response was not treated as execution proof; the request remains queued.", correlationId, cancellationToken);
            return new(requestId, taskRecordId, false, createResult?.IdempotentReplay ?? false, "RDS_TASK_NOT_YET_OBSERVED", "Create was not treated as execution proof; reconciliation will retry the same identity.");
        }

        await store.RecordTaskStatusAsync(requestId, exact[0], query.ReadAt, cancellationToken);
        await workflow.RecordExecutionTransitionAsync(requestId, TransportRequestStatus.SentToRds, cancellationToken);
        await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_CONFIRMED", "RDS_TASK_OBSERVED", "RDS task existence was proven by taskRecordId.", correlationId, cancellationToken);
        return new(requestId, taskRecordId, true, createResult?.IdempotentReplay ?? true, "RDS_TASK_OBSERVED", "RDS task existence was proven by taskRecordId.");
    }

    public async Task ReconcileAsync(int limit, CancellationToken cancellationToken = default)
    {
        var boundedLimit = Math.Clamp(limit, 1, 500);
        var tasks = await store.ListActiveTasksAsync(boundedLimit, cancellationToken);
        foreach (var projection in tasks)
        {
            await using var lease = await store.TryAcquireRequestLeaseAsync(projection.TransportRequestId, cancellationToken);
            if (lease is null)
            {
                continue;
            }

            var correlationId = Guid.NewGuid();
            try
            {
                if (IsNativeMode)
                {
                    var details = await nativeOrderClient!.GetOrderDetailsAsync(projection.TaskRecordId, correlationId, cancellationToken);
                    var status = MapNativeStateToStatus(details.State.Known);
                    var vehicle = ExtractVehicle(details.Raw, projection.AgvId);
                    var task = new RdsWindTaskSnapshot(
                        projection.TaskRecordId,
                        projection.TaskLabel ?? "",
                        status,
                        vehicle,
                        projection.TaskRecordId,
                        details.Raw,
                        details.State.Raw);
                    await ApplyTaskStatusAsync(projection, task, timeProvider.GetUtcNow(), "NATIVE_RECONCILIATION", cancellationToken);
                    if (IsTerminalRdsStatus(status))
                    {
                        await RefreshRelevantWorkSitesAsync(projection.TransportRequestId, correlationId, cancellationToken);
                    }
                }
                else
                {
                    var query = await readClient.QueryWindTasksAsync(new(1, 100, TaskRecordId: projection.TaskRecordId), correlationId, cancellationToken);
                    var exact = query.Data.Tasks.Where(task => string.Equals(task.TaskRecordId, projection.TaskRecordId, StringComparison.Ordinal)).ToArray();
                    if (exact.Length == 1)
                    {
                        await ApplyTaskStatusAsync(projection, exact[0], query.ReadAt, "RECONCILIATION", cancellationToken);
                        if (IsTerminalRdsStatus(exact[0].Status))
                        {
                            await RefreshRelevantWorkSitesAsync(projection.TransportRequestId, correlationId, cancellationToken);
                        }
                    }
                    else if (exact.Length > 1 || query.Data.TotalCount > 1)
                    {
                        throw new RdsExecutionException("RDS_DUPLICATE_TASK_IDENTITY", "RDS returned duplicate task instances for one taskRecordId.");
                    }
                }
            }
            catch (RdsExecutionException error)
            {
                await store.WriteExecutionAuditAsync(projection.TransportRequestId, "RDS_RECONCILIATION_BLOCKED", error.Code, error.Message, correlationId, cancellationToken);
            }
            catch (RdsReadException error)
            {
                await store.WriteExecutionAuditAsync(projection.TransportRequestId, "RDS_RECONCILIATION_DEFERRED", error.Code, error.Message, correlationId, cancellationToken);
            }
            catch (RdsCoreNativeOrderException error)
            {
                await store.WriteExecutionAuditAsync(projection.TransportRequestId, "RDS_RECONCILIATION_BLOCKED", error.Code, error.Message, correlationId, cancellationToken);
            }
        }

        var pendingRefreshes = await store.ListPendingWorkSiteRefreshRequestIdsAsync(boundedLimit, cancellationToken);
        foreach (var requestId in pendingRefreshes)
        {
            await using var lease = await store.TryAcquireRequestLeaseAsync(requestId, cancellationToken);
            if (lease is null)
            {
                continue;
            }

            await RefreshRelevantWorkSitesAsync(requestId, Guid.NewGuid(), cancellationToken);
        }

        if (robotTelemetry is not null && tasks.Count > 0)
        {
            try
            {
                var remainingTimes = await robotTelemetry.GetRobotRemainingTimesAsync(cancellationToken);
                if (remainingTimes.Count > 0)
                {
                    await store.EnrichRemainingTimesAsync(remainingTimes, cancellationToken);
                }
            }
            catch
            {
                // Best-effort enrichment; ETA falls back to elapsed-time estimate.
            }
        }
    }

    public async Task<RdsCancellationResult> CancelAsync(Guid requestId, Guid? actorUserId, Guid correlationId, CancellationToken cancellationToken = default)
    {
        var request = await store.FindRequestAsync(requestId, cancellationToken)
            ?? throw new WorkflowRuleException("REQUEST_NOT_FOUND", "The transport request does not exist.");
        if (TransportStateMachine.CanCancelBeforeExecution(request.Status) || request.Status == TransportRequestStatus.Cancelled)
        {
            var local = await workflow.CancelAsync(requestId, actorUserId, cancellationToken);
            return new(local.Request, false, local.WasAlreadyCancelled);
        }

        if (request.Status is not (TransportRequestStatus.SentToRds or TransportRequestStatus.Running))
        {
            throw new WorkflowRuleException("CANCELLATION_NOT_ALLOWED", "The transport request cannot be cancelled from its current state.");
        }

        await using var lease = await store.TryAcquireRequestLeaseAsync(requestId, cancellationToken);
        if (lease is null)
        {
            return new(request, true, true);
        }

        var task = await store.FindTaskByRequestAsync(requestId, cancellationToken)
            ?? throw new RdsExecutionException("RDS_TASK_NOT_FOUND", "The dispatched request has no persisted RDS task identity.");

        if (IsNativeMode)
        {
            var nativeStopResult = await nativeOrderClient!.TerminateAsync(task.TaskRecordId, correlationId, cancellationToken);
            await store.MarkCancellationRequestedAsync(
                requestId,
                nativeStopResult.RawResponse ?? JsonSerializer.SerializeToElement(new { code = nativeStopResult.Code, message = nativeStopResult.Message }),
                timeProvider.GetUtcNow(),
                cancellationToken);

            var details = await nativeOrderClient.GetOrderDetailsAsync(task.TaskRecordId, correlationId, cancellationToken);
            if (details.State.Known != RdsCoreKnownOrderState.Stopped)
            {
                for (var attempt = 0; attempt < 5 && details.State.Known != RdsCoreKnownOrderState.Stopped; attempt++)
                {
                    await Task.Delay(150, cancellationToken);
                    details = await nativeOrderClient.GetOrderDetailsAsync(task.TaskRecordId, correlationId, cancellationToken);
                }
            }

            if (details.State.Known == RdsCoreKnownOrderState.Stopped)
            {
                var vehicle = ExtractVehicle(details.Raw, task.AgvId);
                var taskSnapshot = new RdsWindTaskSnapshot(
                    task.TaskRecordId,
                    task.TaskLabel ?? "",
                    1001,
                    vehicle,
                    task.TaskRecordId,
                    details.Raw,
                    "STOPPED");
                await store.RecordTaskStatusAsync(requestId, taskSnapshot, timeProvider.GetUtcNow(), cancellationToken);
                var cancelled = await workflow.RecordExecutionOutcomeAsync(
                    requestId,
                    new(TransportRequestStatus.Cancelled, EvidenceType: "RDSCORE_TERMINATE"),
                    cancellationToken);
                return new(cancelled, false, false);
            }

            return new(request, true, !nativeStopResult.CoreAccepted);
        }

        RdsStopTaskResult? stopResult = null;
        try
        {
            stopResult = await executionClient.StopTaskAsync(task.TaskRecordId, correlationId, cancellationToken);
        }
        catch (RdsExecutionException)
        {
            // The stop outcome is ambiguous. Persist intent and let reconciliation prove the physical result.
        }

        await store.MarkCancellationRequestedAsync(
            requestId,
            stopResult?.RawResponse ?? JsonSerializer.SerializeToElement(new { code = "AMBIGUOUS_STOP_RESPONSE" }),
            timeProvider.GetUtcNow(),
            cancellationToken);

        var query = await readClient.QueryWindTasksAsync(new(1, 100, TaskRecordId: task.TaskRecordId), correlationId, cancellationToken);
        var exact = query.Data.Tasks.SingleOrDefault(candidate => string.Equals(candidate.TaskRecordId, task.TaskRecordId, StringComparison.Ordinal));
        if (exact?.Status == 1001)
        {
            await store.RecordTaskStatusAsync(requestId, exact, query.ReadAt, cancellationToken);
            var cancelled = await workflow.RecordExecutionOutcomeAsync(
                requestId,
                new(TransportRequestStatus.Cancelled, EvidenceType: "RDS_QUERY_AFTER_STOP"),
                cancellationToken);
            return new(cancelled, false, false);
        }

        return new(request, true, stopResult is null);
    }

    private async Task<RdsDispatchAttemptResult> DispatchNativeOrderAsync(
        Guid requestId,
        RdsDispatchContext context,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var taskLabel = string.IsNullOrWhiteSpace(context.RdsTaskLabel) ? "TKG_TRANSPORT" : context.RdsTaskLabel.Trim();
        var taskRecordId = requestId.ToString("D");

        var navProfile = await store.GetNavigationProfileSummaryAsync(context.Request.NavigationProfileId, context.Request.VariantId, context.Request.MachineId, cancellationToken);
        if (navProfile is null)
        {
            await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_BLOCKED", "NAVIGATION_PROFILE_UNRESOLVED", "No published NavigationProfile could be resolved for the effective variant.", correlationId, cancellationToken);
            return new(requestId, taskRecordId, false, false, "NAVIGATION_PROFILE_UNRESOLVED", "No published NavigationProfile could be resolved for the effective variant.");
        }

        var requiredMap = navProfile.RobotMapName?.Trim();
        if (string.IsNullOrWhiteSpace(requiredMap))
        {
            await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_BLOCKED", "REQUIRED_MAP_UNRESOLVED", "The resolved NavigationProfile has no configured robot map name.", correlationId, cancellationToken);
            return new(requestId, taskRecordId, false, false, "REQUIRED_MAP_UNRESOLVED", "The resolved NavigationProfile has no configured robot map name.");
        }

        var vehicle = GetLocation(context.TemplateConfig, "vehicle") ?? navProfile.Vehicle;
        if (string.IsNullOrWhiteSpace(vehicle))
        {
            await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_BLOCKED", "VEHICLE_UNRESOLVED", "No vehicle could be resolved from the template or navigation profile.", correlationId, cancellationToken);
            return new(requestId, taskRecordId, false, false, "VEHICLE_UNRESOLVED", "No vehicle could be resolved from the template or navigation profile.");
        }

        IReadOnlyList<RdsCoreRobotTelemetry> robots;
        try
        {
            robots = await nativeOrderClient!.GetRobotsStatusAsync(correlationId, cancellationToken);
        }
        catch (Exception ex)
        {
            await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_BLOCKED", "ROBOT_OFFLINE", $"Failed to query robot telemetry: {ex.Message}", correlationId, cancellationToken);
            return new(requestId, taskRecordId, false, false, "ROBOT_OFFLINE", "RDSCore robot status query failed.");
        }

        var robot = robots.FirstOrDefault(r => string.Equals(r.VehicleId, vehicle, StringComparison.OrdinalIgnoreCase));
        if (robot is null || robot.ConnectionStatus != 1)
        {
            await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_BLOCKED", "ROBOT_OFFLINE", $"Robot '{vehicle}' is offline or not found.", correlationId, cancellationToken);
            return new(requestId, taskRecordId, false, false, "ROBOT_OFFLINE", $"Robot '{vehicle}' is offline or not found.");
        }

        var hasUnreleasedLock = await store.HasUnreleasedMapSwitchLockAsync(requestId, vehicle, cancellationToken);
        if (!robot.Dispatchable)
        {
            // Scenarios B, C, D: If robot is not dispatchable and already on required map, recover lock if TKG owned it
            if (hasUnreleasedLock && string.Equals(robot.CurrentMap?.Trim(), requiredMap, StringComparison.OrdinalIgnoreCase))
            {
                var unlockRecovery = await nativeOrderClient.UnlockAsync([vehicle], correlationId, cancellationToken);
                if (unlockRecovery.CoreAccepted)
                {
                    await store.WriteExecutionAuditAsync(requestId, "MAP_PREP_LOCK_RELEASED", "LOCK_RECOVERY_SUCCESS", "Successfully released post-switchMap lock after crash recovery.", correlationId, cancellationToken);
                    robots = await nativeOrderClient.GetRobotsStatusAsync(correlationId, cancellationToken);
                    robot = robots.FirstOrDefault(r => string.Equals(r.VehicleId, vehicle, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (robot is null || !robot.Dispatchable)
            {
                var errorCode = hasUnreleasedLock ? "MAP_SWITCH_LOCK_RECOVERY_REQUIRED" : "ROBOT_NOT_DISPATCHABLE";
                var errorMessage = hasUnreleasedLock
                    ? $"Robot '{vehicle}' remains locked after a prior map-switch attempt; manual lock recovery is required."
                    : $"Robot '{vehicle}' is not dispatchable.";
                await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_BLOCKED", errorCode, errorMessage, correlationId, cancellationToken);
                return new(requestId, taskRecordId, false, false, errorCode, errorMessage);
            }
        }

        if (robot.IsError || robot.Alarms.Count > 0)
        {
            await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_BLOCKED", "ROBOT_HAS_BLOCKING_ALARM", $"Robot '{vehicle}' has blocking errors/alarms.", correlationId, cancellationToken);
            return new(requestId, taskRecordId, false, false, "ROBOT_HAS_BLOCKING_ALARM", $"Robot '{vehicle}' has blocking errors/alarms.");
        }

        var mapPrepOrderId = RdsCoreNormalOrder.TransitionOrderId(requestId, requiredMap);
        if (!string.IsNullOrWhiteSpace(robot.CurrentOrderId)
            && !string.Equals(robot.CurrentOrderId, taskRecordId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(robot.CurrentOrderId, mapPrepOrderId, StringComparison.OrdinalIgnoreCase))
        {
            await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_BLOCKED", "ROBOT_BUSY_INCOMPATIBLE_ORDER", $"Robot '{vehicle}' is currently executing order '{robot.CurrentOrderId}'.", correlationId, cancellationToken);
            return new(requestId, taskRecordId, false, false, "ROBOT_BUSY_INCOMPATIBLE_ORDER", $"Robot '{vehicle}' is currently executing incompatible order '{robot.CurrentOrderId}'.");
        }

        var currentMap = robot.CurrentMap?.Trim();
        if (!string.Equals(currentMap, requiredMap, StringComparison.OrdinalIgnoreCase))
        {
            if (hasUnreleasedLock)
            {
                // Scenario A: Lock was acquired, but crash occurred before map switch completed
                await store.WriteExecutionAuditAsync(requestId, "MAP_PREP_BLOCKED", "MAP_SWITCH_LOCK_RECOVERY_REQUIRED", $"Robot '{vehicle}' has an unreleased map switch lock from a crashed attempt before map switch completed.", correlationId, cancellationToken);
                return new(requestId, taskRecordId, false, false, "MAP_SWITCH_LOCK_RECOVERY_REQUIRED", $"Robot '{vehicle}' has an unreleased map switch lock from a crashed attempt before map switch completed.");
            }

            var transitionStation = navProfile.SwitchTransitionStation?.Trim();
            if (string.IsNullOrWhiteSpace(transitionStation))
            {
                await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_BLOCKED", "SWITCH_TRANSITION_UNRESOLVED", "Current robot map differs from required map, but no switch transition station is configured in navigation profile.", correlationId, cancellationToken);
                return new(requestId, taskRecordId, false, false, "SWITCH_TRANSITION_UNRESOLVED", "Current robot map differs from required map, but no switch transition station is configured in navigation profile.");
            }

            var isAtStation = string.Equals(robot.CurrentStation?.Trim(), transitionStation, StringComparison.OrdinalIgnoreCase);
            if (!isAtStation)
            {
                RdsCoreOrderDetails? prepDetails = null;
                try
                {
                    prepDetails = await nativeOrderClient.GetOrderDetailsAsync(mapPrepOrderId, correlationId, cancellationToken);
                }
                catch
                {
                    // Order not yet found or query failed
                }

                if (prepDetails?.State.Known == RdsCoreKnownOrderState.Running)
                {
                    await store.WriteExecutionAuditAsync(requestId, "MAP_PREP_AWAITING", "RUNNING", "Preliminary navigation to transition station is running.", correlationId, cancellationToken);
                    return new(requestId, mapPrepOrderId, false, false, "MAP_PREP_RUNNING", "Preliminary navigation to transition station is running.");
                }
                else if (prepDetails?.State.Known != RdsCoreKnownOrderState.Finished)
                {
                    var navBlock = new RdsCoreNavigationBlock(RdsCoreNormalOrder.TransitionBlockId(requestId), transitionStation);
                    var navOrder = RdsCoreNormalOrder.CreateTransition(requestId, requiredMap, navBlock, vehicle, context.TemplatePriority);
                    var navResult = await nativeOrderClient.SetOrderAsync(navOrder, correlationId, cancellationToken);
                    if (!navResult.CoreAccepted && !navResult.DuplicateOrderId)
                    {
                        await store.WriteExecutionAuditAsync(requestId, "MAP_PREP_BLOCKED", navResult.Code?.ToString() ?? "REJECTED", navResult.Message ?? "SetOrder for map prep rejected.", correlationId, cancellationToken);
                        return new(requestId, mapPrepOrderId, false, false, "MAP_PREP_SET_ORDER_REJECTED", navResult.Message ?? "SetOrder for transition navigation was rejected.");
                    }

                    await store.WriteExecutionAuditAsync(requestId, "MAP_PREP_DISPATCHED", "TRANSITION_NAVIGATION", $"Dispatched preliminary navigation to transition site '{transitionStation}'.", correlationId, cancellationToken);
                    return new(requestId, mapPrepOrderId, true, navResult.DuplicateOrderId, "MAP_PREP_DISPATCHED", "Dispatched preliminary navigation to transition station.");
                }
            }

            // Requirement 5: Transition Arrival Verification
            // Re-query /robotsStatus and strictly confirm current_station == transitionStation and robot health
            try
            {
                robots = await nativeOrderClient.GetRobotsStatusAsync(correlationId, cancellationToken);
            }
            catch (Exception ex)
            {
                await store.WriteExecutionAuditAsync(requestId, "MAP_PREP_BLOCKED", "SWITCH_TRANSITION_NOT_CONFIRMED", $"Failed to query robot status for transition arrival verification: {ex.Message}", correlationId, cancellationToken);
                return new(requestId, taskRecordId, false, false, "SWITCH_TRANSITION_NOT_CONFIRMED", "Failed to query robot status for transition arrival verification.");
            }

            robot = robots.FirstOrDefault(r => string.Equals(r.VehicleId, vehicle, StringComparison.OrdinalIgnoreCase));
            if (robot is null
                || robot.ConnectionStatus != 1
                || !robot.Dispatchable
                || robot.IsError
                || robot.Alarms.Count > 0
                || (!string.IsNullOrEmpty(robot.CurrentOrderId) && !string.Equals(robot.CurrentOrderId, mapPrepOrderId, StringComparison.OrdinalIgnoreCase))
                || !string.Equals(robot.CurrentStation?.Trim(), transitionStation, StringComparison.OrdinalIgnoreCase))
            {
                await store.WriteExecutionAuditAsync(requestId, "MAP_PREP_BLOCKED", "SWITCH_TRANSITION_NOT_CONFIRMED", $"Transition station arrival could not be confirmed. Station='{robot?.CurrentStation}', expected='{transitionStation}'.", correlationId, cancellationToken);
                return new(requestId, taskRecordId, false, false, "SWITCH_TRANSITION_NOT_CONFIRMED", $"Transition station arrival could not be confirmed. Robot station is '{robot?.CurrentStation}', expected '{transitionStation}'.");
            }

            RdsCoreSimpleResult lockResult;
            try
            {
                lockResult = await nativeOrderClient.LockAsync([vehicle], correlationId, cancellationToken);
            }
            catch (Exception ex)
            {
                await store.WriteExecutionAuditAsync(requestId, "MAP_PREP_BLOCKED", "LOCK_FAILED", ex.Message, correlationId, cancellationToken);
                return new(requestId, taskRecordId, false, false, "LOCK_FAILED", ex.Message);
            }

            if (!lockResult.CoreAccepted)
            {
                await store.WriteExecutionAuditAsync(requestId, "MAP_PREP_BLOCKED", "LOCK_FAILED", lockResult.Message ?? "Lock vehicle rejected before switchMap.", correlationId, cancellationToken);
                return new(requestId, taskRecordId, false, false, "LOCK_FAILED", lockResult.Message ?? "Lock vehicle rejected before switchMap.");
            }

            await store.WriteExecutionAuditAsync(requestId, "MAP_PREP_LOCK_ACQUIRED", "LOCK_ACQUIRED", $"Lock acquired on vehicle '{vehicle}' for map switch to '{requiredMap}'.", correlationId, cancellationToken);

            RdsCoreSimpleResult switchResult;
            try
            {
                switchResult = await nativeOrderClient.SwitchMapAsync(vehicle, requiredMap, correlationId, cancellationToken);
            }
            catch (Exception ex)
            {
                await store.WriteExecutionAuditAsync(requestId, "MAP_PREP_BLOCKED", "SWITCHMAP_FAILED", ex.Message, correlationId, cancellationToken);
                return new(requestId, taskRecordId, false, false, "SWITCHMAP_FAILED", ex.Message);
            }
            finally
            {
                try
                {
                    var unlockResult = await nativeOrderClient.UnlockAsync([vehicle], correlationId, cancellationToken);
                    if (unlockResult.CoreAccepted)
                    {
                        await store.WriteExecutionAuditAsync(requestId, "MAP_PREP_LOCK_RELEASED", "UNLOCK_SUCCESS", $"Lock released on vehicle '{vehicle}'.", correlationId, cancellationToken);
                    }
                }
                catch
                {
                    // If unlock throws or fails, MAP_PREP_LOCK_RELEASED is not written, enabling safe recovery on restart
                }
            }

            if (!switchResult.CoreAccepted)
            {
                await store.WriteExecutionAuditAsync(requestId, "MAP_PREP_BLOCKED", "SWITCHMAP_REJECTED", switchResult.Message ?? "SwitchMap rejected by RDSCore.", correlationId, cancellationToken);
                return new(requestId, taskRecordId, false, false, "SWITCHMAP_REJECTED", switchResult.Message ?? "SwitchMap rejected by RDSCore.");
            }

            try
            {
                robots = await nativeOrderClient.GetRobotsStatusAsync(correlationId, cancellationToken);
            }
            catch (Exception ex)
            {
                await store.WriteExecutionAuditAsync(requestId, "MAP_PREP_BLOCKED", "POST_SWITCH_TELEMETRY_FAILED", ex.Message, correlationId, cancellationToken);
                return new(requestId, taskRecordId, false, false, "POST_SWITCH_TELEMETRY_FAILED", ex.Message);
            }
            robot = robots.FirstOrDefault(r => string.Equals(r.VehicleId, vehicle, StringComparison.OrdinalIgnoreCase));
            if (robot is null || !string.Equals(robot.CurrentMap?.Trim(), requiredMap, StringComparison.OrdinalIgnoreCase))
            {
                await store.WriteExecutionAuditAsync(requestId, "MAP_PREP_BLOCKED", "SWITCHMAP_VERIFICATION_FAILED", $"Robot map is '{robot?.CurrentMap}', expected '{requiredMap}'.", correlationId, cancellationToken);
                return new(requestId, taskRecordId, false, false, "SWITCHMAP_VERIFICATION_FAILED", "Robot did not switch to the required map.");
            }

            if (robot.IsError || robot.Alarms.Count > 0)
            {
                await store.WriteExecutionAuditAsync(requestId, "MAP_PREP_BLOCKED", "POST_SWITCH_ALARM", "Robot has alarms following map switch.", correlationId, cancellationToken);
                return new(requestId, taskRecordId, false, false, "POST_SWITCH_ALARM", "Robot has alarms following map switch.");
            }

            await store.WriteExecutionAuditAsync(requestId, "MAP_PREP_CONFIRMED", "SWITCHMAP_SUCCESS", $"Successfully switched robot map to '{requiredMap}'.", correlationId, cancellationToken);
        }

        var pickupLocation = GetLocation(context.TemplateConfig, "pickupLocation")
            ?? context.Source.SiteId;
        var dropoffLocation = GetLocation(context.TemplateConfig, "dropoffLocation")
            ?? context.Destination.SiteId;

        if (string.IsNullOrWhiteSpace(pickupLocation) || string.IsNullOrWhiteSpace(dropoffLocation))
        {
            await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_BLOCKED", "RDS_DISPATCH_ENDPOINT_UNRESOLVED", "Native order dispatch requires resolved pickup and dropoff locations from the task template or layout slot mapping.", correlationId, cancellationToken);
            return new(requestId, taskRecordId, false, false, "RDS_DISPATCH_ENDPOINT_UNRESOLVED", "Native order dispatch requires resolved pickup and dropoff locations from the task template or layout slot mapping.");
        }

        var pickupBlock = new RdsCoreNavigationBlock(RdsCoreNormalOrder.PickupBlockId(requestId), pickupLocation);
        var dropoffBlock = new RdsCoreNavigationBlock(RdsCoreNormalOrder.DropoffBlockId(requestId), dropoffLocation);
        var order = RdsCoreNormalOrder.Create(
            requestId,
            pickupBlock,
            dropoffBlock,
            vehicle: vehicle,
            priority: context.TemplatePriority);

        var requestPayload = JsonSerializer.SerializeToElement(order);
        await store.EnsureTaskRecordAsync(requestId, taskRecordId, taskLabel, requestPayload, cancellationToken);
        await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_ATTEMPTED", "RDS_SET_ORDER", "Native order dispatch was attempted with the immutable TKG request identity.", correlationId, cancellationToken);

        RdsCoreSetOrderResult? setResult = null;
        try
        {
            setResult = await nativeOrderClient!.SetOrderAsync(order, correlationId, cancellationToken);
            await store.RecordDispatchResponseAsync(
                requestId,
                setResult.RawResponse ?? JsonSerializer.SerializeToElement(new { code = setResult.Code, message = setResult.Message }),
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
        catch (RdsCoreNativeOrderException)
        {
            // A lost HTTP response or timeout is ambiguous. Query by taskRecordId before deciding whether to retry.
        }

        if (setResult is not null && !setResult.CoreAccepted && !setResult.DuplicateOrderId)
        {
            await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_BLOCKED", setResult.Code?.ToString() ?? "REJECTED", setResult.Message ?? "SetOrder was rejected.", correlationId, cancellationToken);
            return new(requestId, taskRecordId, false, false, "RDS_SET_ORDER_REJECTED", setResult.Message ?? "SetOrder was rejected.");
        }

        RdsCoreOrderDetails? details = null;
        try
        {
            details = await nativeOrderClient!.GetOrderDetailsAsync(taskRecordId, correlationId, cancellationToken);
        }
        catch (RdsCoreNativeOrderException)
        {
            // Order details query failed or order not yet observed. Request remains queued for reconciliation.
        }

        if (details is null)
        {
            await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_UNCONFIRMED", "RDS_TASK_NOT_YET_OBSERVED", "The native order response was not confirmed; the request remains queued.", correlationId, cancellationToken);
            return new(requestId, taskRecordId, false, setResult?.DuplicateOrderId ?? false, "RDS_TASK_NOT_YET_OBSERVED", "SetOrder was not confirmed; reconciliation will retry the same identity.");
        }

        var status = MapNativeStateToStatus(details.State.Known);
        var observedVehicle = ExtractVehicle(details.Raw, vehicle);
        var taskSnapshot = new RdsWindTaskSnapshot(
            taskRecordId,
            taskLabel,
            status,
            observedVehicle,
            taskRecordId,
            details.Raw,
            details.State.Raw);

        await store.RecordTaskStatusAsync(requestId, taskSnapshot, timeProvider.GetUtcNow(), cancellationToken);
        await workflow.RecordExecutionTransitionAsync(requestId, TransportRequestStatus.SentToRds, cancellationToken);
        if (details.State.Known == RdsCoreKnownOrderState.Running)
        {
            await workflow.RecordExecutionTransitionAsync(requestId, TransportRequestStatus.Running, cancellationToken);
        }
        await store.WriteExecutionAuditAsync(requestId, "RDS_DISPATCH_CONFIRMED", "RDS_NATIVE_ORDER_OBSERVED", "RDS native order existence was proven by taskRecordId.", correlationId, cancellationToken);
        return new(requestId, taskRecordId, true, setResult?.DuplicateOrderId ?? true, "RDS_TASK_OBSERVED", "RDS native order existence was proven by taskRecordId.");
    }

    private static string? GetLocation(JsonElement config, string propertyName)
    {
        if (config.ValueKind == JsonValueKind.Object && config.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            var s = prop.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }
        return null;
    }

    private static string? ExtractVehicle(JsonElement raw, string? fallback)
    {
        if (raw.ValueKind == JsonValueKind.Object)
        {
            if (raw.TryGetProperty("candidateName", out var cProp) && cProp.ValueKind == JsonValueKind.String)
            {
                var s = cProp.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
            if (raw.TryGetProperty("vehicle", out var vProp) && vProp.ValueKind == JsonValueKind.String)
            {
                var s = vProp.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
        }
        return fallback;
    }

    private static int? MapNativeStateToStatus(RdsCoreKnownOrderState state) => state switch
    {
        RdsCoreKnownOrderState.Running => 1000,
        RdsCoreKnownOrderState.Finished => 1003,
        RdsCoreKnownOrderState.Stopped => 1001,
        RdsCoreKnownOrderState.Failed or RdsCoreKnownOrderState.Error => 1004,
        _ => null
    };

    public async Task<RdsCallbackResult> ProcessCallbackAsync(RdsCallbackEvent callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var eventType = callback.EventType.Trim();
        if (eventType is not ("taskDone" or "taskFailed" or "taskStopped" or "workSiteChanged"))
        {
            return new(true, false, "RDS_EVENT_IGNORED", "Unknown callback events do not mutate domain state.", null);
        }

        if (eventType == "workSiteChanged")
        {
            return new(true, false, "RDS_WORKSITE_EVENT_ACCEPTED", "WorkSite evidence will be refreshed by the read-side synchronization path.", null);
        }

        var taskRecordId = FindText(callback.RawPayload, "taskRecordId") ?? FindText(callback.RawPayload, "id");
        if (string.IsNullOrWhiteSpace(taskRecordId))
        {
            return new(true, false, "RDS_TASK_ID_UNRESOLVED", "The callback did not expose a taskRecordId.", null);
        }

        var projection = await store.FindTaskByRecordIdAsync(taskRecordId, cancellationToken);
        if (projection is null)
        {
            return new(true, false, "RDS_TASK_UNKNOWN", "The callback identity is not owned by TKG.", taskRecordId);
        }

        var status = eventType switch
        {
            "taskDone" => 1003,
            "taskFailed" => Number(callback.RawPayload, "status") is 1004 or 1005 or 1006 ? Number(callback.RawPayload, "status")!.Value : 1004,
            "taskStopped" => 1001,
            _ => (int?)null
        };
        var task = new RdsWindTaskSnapshot(
            taskRecordId,
            FindText(callback.RawPayload, "taskLabel") ?? projection.TaskLabel,
            status,
            FindText(callback.RawPayload, "agvId"),
            FindText(callback.RawPayload, "outOrderNo"),
            callback.RawPayload.Clone(),
            FindText(callback.RawPayload, "endedReason"));
        await ApplyTaskStatusAsync(projection, task, timeProvider.GetUtcNow(), $"CALLBACK_{eventType}", cancellationToken);
        await store.WriteExecutionAuditAsync(
            projection.TransportRequestId,
            "RDS_CALLBACK_APPLIED",
            eventType,
            "Authenticated RDS callback evidence was applied idempotently.",
            Guid.NewGuid(),
            cancellationToken);
        return new(true, true, "RDS_EVENT_APPLIED", "The callback was applied idempotently.", taskRecordId);
    }

    public async Task<RdsLoadContentSyncResult> SynchronizeLoadContentAsync(Guid slotId, LoadState loadState, Guid correlationId, CancellationToken cancellationToken = default)
    {
        var configured = loadState switch
        {
            LoadState.Empty => settings.EmptyContentValue,
            LoadState.Full => settings.FullContentValue,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(configured))
        {
            return new("NOT_CONFIGURED", null, null, "TKG load state was updated; RDS content semantics are not configured.");
        }

        var site = await store.FindMappedWorkSiteAsync(slotId, cancellationToken);
        if (site is null)
        {
            return new("NOT_MAPPED", null, configured, "TKG load state was updated; this slot has no RDS WorkSite mapping.");
        }

        if (site.Type != 1 || site.Disabled == 1 || site.SyncFailed == 1)
        {
            return new("NOT_READY", site.SiteId, configured, "TKG load state was updated; the mapped physical WorkSite is not execution-ready.");
        }

        await executionClient.UpdateContentAsync(site.SiteId, configured, correlationId, cancellationToken);
        return new("SYNCHRONIZED", site.SiteId, configured, "Only RDS content was changed; filled was not modified.");
    }

    private async Task ApplyTaskStatusAsync(
        RdsTaskProjection projection,
        RdsWindTaskSnapshot task,
        DateTimeOffset syncedAt,
        string evidence,
        CancellationToken cancellationToken)
    {
        await store.RecordTaskStatusAsync(projection.TransportRequestId, task, syncedAt, cancellationToken);
        switch (task.Status)
        {
            case 1000:
                if (projection.RequestStatus == TransportRequestStatus.SentToRds)
                {
                    await workflow.RecordExecutionTransitionAsync(projection.TransportRequestId, TransportRequestStatus.Running, cancellationToken);
                }
                break;
            case 1002:
                break;
            case 1003:
                if (projection.RequestStatus == TransportRequestStatus.SentToRds)
                {
                    await workflow.RecordExecutionTransitionAsync(projection.TransportRequestId, TransportRequestStatus.Running, cancellationToken);
                }
                if (projection.RequestStatus is TransportRequestStatus.SentToRds or TransportRequestStatus.Running)
                {
                    await workflow.RecordExecutionTransitionAsync(projection.TransportRequestId, TransportRequestStatus.Completed, cancellationToken);
                }
                break;
            case 1001:
                await workflow.RecordExecutionOutcomeAsync(
                    projection.TransportRequestId,
                    projection.CancellationRequested
                        ? new(TransportRequestStatus.Cancelled, EvidenceType: evidence)
                        : new(TransportRequestStatus.Failed, "RDS_TASK_TERMINATED", task.EndedReason, evidence),
                    cancellationToken);
                break;
            case 1004:
            case 1005:
            case 1006:
                await workflow.RecordExecutionOutcomeAsync(
                    projection.TransportRequestId,
                    new(TransportRequestStatus.Failed, $"RDS_TASK_STATUS_{task.Status}", task.EndedReason, evidence),
                    cancellationToken);
                break;
        }
    }

    private async Task RefreshRelevantWorkSitesAsync(
        Guid requestId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var request = await store.FindRequestAsync(requestId, cancellationToken);
        if (request?.NavigationProfileId is not Guid navigationProfileId)
        {
            await store.WriteExecutionAuditAsync(
                requestId,
                "RDS_WORKSITE_REFRESH_SKIPPED",
                "NAVIGATION_PROFILE_NOT_CONFIGURED",
                "Terminal task evidence was applied, but the request has no navigation profile to refresh.",
                correlationId,
                cancellationToken);
            return;
        }

        try
        {
            await workSiteReconciler.SyncWorkSitesAsync(navigationProfileId, correlationId, cancellationToken);
            await store.WriteExecutionAuditAsync(
                requestId,
                "RDS_WORKSITE_REFRESHED",
                "RDS_WORKSITE_EVIDENCE_REFRESHED",
                "Relevant RDS WorkSite evidence was refreshed after a terminal task status.",
                correlationId,
                cancellationToken);
        }
        catch (RdsReadException error)
        {
            await store.WriteExecutionAuditAsync(
                requestId,
                "RDS_WORKSITE_REFRESH_DEFERRED",
                error.Code,
                "Terminal workflow evidence was retained; WorkSite refresh will retry without guessing physical state.",
                correlationId,
                cancellationToken);
        }
        catch (RdsIntegrationRuleException error)
        {
            await store.WriteExecutionAuditAsync(
                requestId,
                "RDS_WORKSITE_REFRESH_DEFERRED",
                error.Code,
                "Terminal workflow evidence was retained; WorkSite refresh will retry without guessing physical state.",
                correlationId,
                cancellationToken);
        }
    }

    private static bool IsTerminalRdsStatus(int? status) => status is 1001 or 1003 or 1004 or 1005 or 1006;

    private void ValidateReferencedEndpoints(RdsDispatchContext context, IReadOnlyDictionary<string, object?> inputParams)
    {
        if (context.Source.SlotRole == "MACHINE" || context.Destination.SlotRole == "MACHINE")
        {
            throw new RdsExecutionException("MACHINE_REGION_FORBIDDEN", "Display-only MACHINE regions can never reach RDS dispatch.");
        }

        var raw = context.TemplateConfig.GetProperty("rdsInputBindings");
        foreach (var binding in raw.EnumerateArray())
        {
            var source = binding.GetProperty("source").GetString();
            if (source == "SOURCE_RDS_SITE_ID") ValidateEndpoint(context.Source, "source");
            if (source == "DESTINATION_RDS_SITE_ID") ValidateEndpoint(context.Destination, "destination");
        }

        _ = inputParams;
    }

    private void ValidateEndpoint(RdsDispatchEndpoint endpoint, string name)
    {
        if (endpoint.SlotRole == "MACHINE") throw new RdsExecutionException("MACHINE_REGION_FORBIDDEN", $"The {name} endpoint is a display-only MACHINE region.");
        if (!endpoint.IsMapped || string.IsNullOrWhiteSpace(endpoint.SiteId)) throw new RdsExecutionException("RDS_WORKSITE_NOT_MAPPED", $"The {name} endpoint has no explicit WorkSite mapping.");
        if (endpoint.Type != 1) throw new RdsExecutionException("RDS_WORKSITE_NOT_PHYSICAL", $"The {name} WorkSite is not physical.");
        if (endpoint.Disabled == 1) throw new RdsExecutionException("RDS_WORKSITE_DISABLED", $"The {name} WorkSite is disabled.");
        if (endpoint.SyncFailed == 1) throw new RdsExecutionException("RDS_WORKSITE_SYNC_FAILED", $"The {name} WorkSite reports syncFailed.");
        if (endpoint.Locked == 1) throw new RdsExecutionException("RDS_WORKSITE_LOCKED", $"The {name} WorkSite is locked.");
        if (!endpoint.LastSyncAt.HasValue || timeProvider.GetUtcNow() - endpoint.LastSyncAt.Value > settings.WorkSiteFreshness)
            throw new RdsExecutionException("RDS_WORKSITE_STALE", $"The {name} WorkSite evidence is stale.");
    }

    private static string? FindText(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
                }
                var nested = FindText(property.Value, name);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var nested = FindText(item, name);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        return null;
    }

    private static int? Number(JsonElement value, string name)
    {
        var text = FindText(value, name);
        return int.TryParse(text, out var number) ? number : null;
    }
}
