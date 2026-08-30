using System.Text.Json;

namespace TkgPda.Application;

public sealed record RdsWorkSiteSnapshot(
    string SiteId,
    string? Area,
    string? GroupName,
    int? Type,
    int? Filled,
    int? Locked,
    string? LockedBy,
    int? Disabled,
    int? SyncFailed,
    string? Content,
    string? AgvId,
    JsonElement RawState);

public sealed record RdsWindTaskQuery(
    int CurrentPage = 1,
    int PageSize = 20,
    string? TaskRecordId = null,
    string? TaskLabel = null,
    int? Status = null,
    string? AgvId = null);

public sealed record RdsWindTaskSnapshot(
    string? TaskRecordId,
    string? TaskLabel,
    int? Status,
    string? AgvId,
    string? OutOrderNo,
    JsonElement RawState,
    string? EndedReason = null);

public sealed record RdsWindTaskPage(
    int TotalCount,
    int CurrentPage,
    int PageSize,
    int TotalPage,
    IReadOnlyList<RdsWindTaskSnapshot> Tasks);

public sealed record RdsReadResult<T>(
    T Data,
    int BusinessCode,
    string BusinessMessage,
    string Endpoint,
    Guid CorrelationId,
    DateTimeOffset ReadAt);

// Customer Monitoring — Robot Alarm Visibility: flattened current alarm
// entries from GET /api/agv-report/core. Severity comes from the RDS bucket
// (fatals/errors/warnings/notices) the item was found in. An alarm is
// active only while it appears in the current snapshot; RDS exposes no
// explicit active/cleared flag or incident history for it.
public sealed record RdsRobotAlarmSnapshot(
    string RobotId,
    string Severity,
    string Code,
    string? Description,
    string? OccurredAt,
    int? OccurrenceCount);

// Per-robot state carried by the same GET /api/agv-report/core response,
// kept separate from alarm items since it describes the robot, not an alarm.
public sealed record RdsRobotStateSnapshot(
    string RobotId,
    string? Model,
    bool? Dispatchable,
    string? UndispatchableReason);

public sealed record RdsRobotAlarmReport(
    IReadOnlyList<RdsRobotAlarmSnapshot> Alarms,
    IReadOnlyList<RdsRobotStateSnapshot> Robots);

public interface IRdsReadClient
{
    bool Enabled { get; }
    string? SafeBaseAddress { get; }
    DateTimeOffset? LastSuccessfulRead { get; }

    Task<RdsReadResult<IReadOnlyList<RdsWorkSiteSnapshot>>> GetWorkSitesAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default);

    Task<RdsReadResult<RdsWindTaskPage>> QueryWindTasksAsync(
        RdsWindTaskQuery query,
        Guid correlationId,
        CancellationToken cancellationToken = default);

    Task<RdsReadResult<RdsRobotAlarmReport>> GetRobotAlarmsAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default);
}

public sealed class RdsReadException(
    string code,
    string message,
    string endpoint,
    Guid correlationId,
    int? businessCode = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public string Endpoint { get; } = endpoint;
    public Guid CorrelationId { get; } = correlationId;
    public int? BusinessCode { get; } = businessCode;
}

public sealed class RdsIntegrationRuleException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public sealed record RdsConnectionStatusRecord(
    bool Enabled,
    bool Reachable,
    bool Stale,
    DateTimeOffset? LastSuccessfulRead,
    string? BaseAddressDisplaySafe,
    int? BusinessStatus,
    string? BusinessMessage,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record RdsNavigationProfileRecord(
    Guid Id,
    Guid MachineId,
    string MachineCode,
    string Code,
    string Name,
    int Version,
    string Status);

public sealed record RdsMirroredWorkSiteRecord(
    Guid Id,
    Guid NavigationProfileId,
    string SiteId,
    string? Area,
    string? GroupName,
    int? Type,
    int? Filled,
    int? Locked,
    int? Disabled,
    int? SyncFailed,
    string? Content,
    string? AgvId,
    DateTimeOffset? LastSyncAt,
    bool ExecutionReady);

public sealed record RdsWorkSiteFilter(
    Guid NavigationProfileId,
    string? SiteId = null,
    string? Area = null,
    string? GroupName = null,
    int? Type = null,
    int? Filled = null,
    int? Locked = null,
    int? Disabled = null,
    int? SyncFailed = null);

public sealed record RdsSlotMappingRecord(
    Guid SlotId,
    Guid LayoutId,
    string SlotCode,
    string SlotDisplayName,
    string SlotRole,
    Guid MachineId,
    Guid? RdsWorkSiteId,
    string? RdsSiteId,
    Guid? NavigationProfileId,
    int? RdsType,
    int? Filled,
    int? Locked,
    int? Disabled,
    int? SyncFailed,
    DateTimeOffset? LastSyncAt,
    bool ExecutionReady);

public sealed record RdsWorkSiteSyncRecord(
    Guid NavigationProfileId,
    int Received,
    int Created,
    int Updated,
    DateTimeOffset SyncedAt,
    int BusinessCode,
    string BusinessMessage);

public sealed record RdsRobotAlarmAdminRecord(
    string RobotId,
    string? RobotModel,
    string Severity,
    string Code,
    string? Description,
    int? OccurrenceCount,
    string? OccurredAt,
    bool? Dispatchable,
    string? UndispatchableReason);

public sealed record RdsRobotAlarmOperatorSummary(
    bool HasActiveRobotAlarm,
    string? HighestSeverity,
    int ActiveAlarmCount);

public interface IRdsIntegrationStore
{
    Task<IReadOnlyList<RdsNavigationProfileRecord>> ListNavigationProfilesAsync(CancellationToken cancellationToken = default);
    Task<(int Created, int Updated)> SyncWorkSitesAsync(Guid navigationProfileId, IReadOnlyList<RdsWorkSiteSnapshot> workSites, DateTimeOffset syncedAt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RdsMirroredWorkSiteRecord>> ListWorkSitesAsync(RdsWorkSiteFilter filter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RdsSlotMappingRecord>> ListSlotMappingsAsync(Guid? layoutId, CancellationToken cancellationToken = default);
    Task<RdsSlotMappingRecord> SetSlotMappingAsync(Guid slotId, Guid rdsWorkSiteId, AdminMutationContext context, CancellationToken cancellationToken = default);
    Task RemoveSlotMappingAsync(Guid slotId, AdminMutationContext context, CancellationToken cancellationToken = default);
}

public interface IRdsWorkSiteReconciler
{
    Task<RdsWorkSiteSyncRecord> SyncWorkSitesAsync(
        Guid navigationProfileId,
        Guid correlationId,
        CancellationToken cancellationToken = default);
}

public sealed class RdsIntegrationService(IRdsReadClient client, IRdsIntegrationStore store, TimeProvider timeProvider) : IRdsWorkSiteReconciler
{
    private readonly SemaphoreSlim syncGate = new(1, 1);

    public async Task<RdsConnectionStatusRecord> GetStatusAsync(Guid correlationId, CancellationToken cancellationToken = default)
    {
        if (!client.Enabled)
        {
            return new(false, false, true, client.LastSuccessfulRead, client.SafeBaseAddress, null, null, "RDS_DISABLED", "RDS read integration is disabled.");
        }

        try
        {
            var result = await client.GetWorkSitesAsync(correlationId, cancellationToken);
            return new(true, true, false, result.ReadAt, client.SafeBaseAddress, result.BusinessCode, result.BusinessMessage, null, null);
        }
        catch (RdsReadException error)
        {
            return new(true, false, true, client.LastSuccessfulRead, client.SafeBaseAddress, error.BusinessCode, error.Message, error.Code, "RDS read service is unavailable; last persisted evidence was retained.");
        }
    }

    public Task<IReadOnlyList<RdsNavigationProfileRecord>> ListNavigationProfilesAsync(CancellationToken cancellationToken = default) =>
        store.ListNavigationProfilesAsync(cancellationToken);

    public Task<IReadOnlyList<RdsMirroredWorkSiteRecord>> ListWorkSitesAsync(RdsWorkSiteFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.NavigationProfileId == Guid.Empty)
        {
            throw new RdsIntegrationRuleException("NAVIGATION_PROFILE_REQUIRED", "An explicit navigation profile is required.");
        }

        return store.ListWorkSitesAsync(filter, cancellationToken);
    }

    public Task<IReadOnlyList<RdsSlotMappingRecord>> ListSlotMappingsAsync(Guid? layoutId, CancellationToken cancellationToken = default) =>
        store.ListSlotMappingsAsync(layoutId, cancellationToken);

    public async Task<RdsWorkSiteSyncRecord> SyncWorkSitesAsync(Guid navigationProfileId, Guid correlationId, CancellationToken cancellationToken = default)
    {
        if (navigationProfileId == Guid.Empty)
        {
            throw new RdsIntegrationRuleException("NAVIGATION_PROFILE_REQUIRED", "An explicit navigation profile is required.");
        }

        if (!client.Enabled)
        {
            throw new RdsReadException("RDS_DISABLED", "RDS read integration is disabled.", "/api/work-sites/sites", correlationId);
        }

        if (!await syncGate.WaitAsync(0, cancellationToken))
        {
            throw new RdsIntegrationRuleException("RDS_SYNC_IN_PROGRESS", "A WorkSite synchronization is already running.");
        }

        try
        {
            // Read RDS completely before opening the TKG transaction. A failed read never erases the mirror.
            var result = await client.GetWorkSitesAsync(correlationId, cancellationToken);
            var syncedAt = timeProvider.GetUtcNow();
            var counts = await store.SyncWorkSitesAsync(navigationProfileId, result.Data, syncedAt, cancellationToken);
            return new(navigationProfileId, result.Data.Count, counts.Created, counts.Updated, syncedAt, result.BusinessCode, result.BusinessMessage);
        }
        finally
        {
            syncGate.Release();
        }
    }

    public Task<RdsSlotMappingRecord> SetSlotMappingAsync(Guid slotId, Guid rdsWorkSiteId, AdminMutationContext context, CancellationToken cancellationToken = default)
    {
        if (slotId == Guid.Empty || rdsWorkSiteId == Guid.Empty)
        {
            throw new RdsIntegrationRuleException("ID_REQUIRED", "Slot and RDS WorkSite identifiers are required.");
        }

        return store.SetSlotMappingAsync(slotId, rdsWorkSiteId, context, cancellationToken);
    }

    public Task RemoveSlotMappingAsync(Guid slotId, AdminMutationContext context, CancellationToken cancellationToken = default)
    {
        if (slotId == Guid.Empty)
        {
            throw new RdsIntegrationRuleException("ID_REQUIRED", "Slot identifier is required.");
        }

        return store.RemoveSlotMappingAsync(slotId, context, cancellationToken);
    }

    // Customer Monitoring — Robot Alarm Visibility: Admin sees the full
    // current-alarm detail; robot state (model/dispatchable/reason) is
    // joined in from the same read since it is per-robot, not per-alarm.
    public async Task<IReadOnlyList<RdsRobotAlarmAdminRecord>> ListRobotAlarmsForAdminAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var result = await client.GetRobotAlarmsAsync(correlationId, cancellationToken);
        var robotsById = result.Data.Robots.ToDictionary(robot => robot.RobotId, StringComparer.Ordinal);
        return result.Data.Alarms
            .Select(alarm =>
            {
                robotsById.TryGetValue(alarm.RobotId, out var robot);
                return new RdsRobotAlarmAdminRecord(
                    alarm.RobotId,
                    robot?.Model,
                    alarm.Severity,
                    alarm.Code,
                    alarm.Description,
                    alarm.OccurrenceCount,
                    alarm.OccurredAt,
                    robot?.Dispatchable,
                    robot?.UndispatchableReason);
            })
            .ToList();
    }

    // Coarse Operator projection: no verified TransportRequest -> assigned
    // robot mapping exists yet (RdsTaskProjection.AgvId is not confirmed to
    // share the agv-report uuid/vehicle_id identity space, and no store
    // query scopes active tasks by machine), so per-machine filtering is
    // deliberately not attempted here to avoid inventing that relationship.
    public async Task<RdsRobotAlarmOperatorSummary> GetRobotAlarmSummaryForOperatorAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var result = await client.GetRobotAlarmsAsync(correlationId, cancellationToken);
        var alarms = result.Data.Alarms;
        var highestSeverity = alarms.Count == 0
            ? null
            : alarms
                .Select(alarm => alarm.Severity)
                .OrderByDescending(severity => Array.IndexOf(SeverityRank, severity))
                .First();
        return new RdsRobotAlarmOperatorSummary(alarms.Count > 0, highestSeverity, alarms.Count);
    }

    private static readonly string[] SeverityRank = ["notice", "warning", "error", "fatal"];
}
