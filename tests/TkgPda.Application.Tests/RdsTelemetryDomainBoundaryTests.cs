using System.Text.Json;
using TkgPda.Application;
using TkgPda.Domain;
using Xunit;

namespace TkgPda.Application.Tests;

public sealed class RdsTelemetryDomainBoundaryTests
{
    [Fact]
    public void Rds_reported_payload_loaded_telemetry_never_mutates_or_influences_tkg_load_state()
    {
        // 1. Domain invariant: TKG inventory LoadState (EMPTY / FULL / UNKNOWN) is authoritative.
        // It must NEVER be inferred or modified from external SEER robot payload telemetry.
        var initialSlotState = new SlotState(OccupancyState.Occupied, LoadState.Empty, Guid.NewGuid(), Guid.NewGuid());

        // Diagnostic telemetry reports that SEER robot has payload loaded (isLoaded = true)
        var telemetry = new RdsRobotStateSnapshot(
            RobotId: "robot_101",
            Model: "AMB-150",
            Dispatchable: true,
            UndispatchableReason: null,
            RdsReportedPayloadLoaded: true);

        Assert.True(telemetry.RdsReportedPayloadLoaded);

        // Domain load state transitions are strictly governed by authorized operator container actions
        // and deterministic domain rules, NEVER by telemetry.
        var fullResolution = ContainerActionResolutionPolicy.ResolveSingle(
            OperatorContainerAction.Full,
            initialSlotState.Occupancy,
            initialSlotState.Load);

        Assert.Equal(TransportTaskType.RemoveFull, fullResolution.Operation);
        Assert.True(fullResolution.MarkFullBeforeCreate);

        // Even if telemetry claims payload is unloaded/false, domain state remains unaffected.
        var telemetryEmpty = telemetry with { RdsReportedPayloadLoaded = false };
        Assert.False(telemetryEmpty.RdsReportedPayloadLoaded);
        Assert.Equal(LoadState.Empty, initialSlotState.Load);
    }

    [Fact]
    public async Task Active_native_order_may_have_current_map_differ_from_pre_dispatch_map_without_invariant_violation()
    {
        // RDSCore route planning may legitimately perform multi-map route transitions during an active native order.
        // The service must represent this cleanly without error or invariant violation.
        var preDispatchMap = "26.7.2026_map_optimak_1";
        var runtimeRouteMap = "26.7.2026_map_optimak";

        var robot = new RdsRobotStateSnapshot(
            RobotId: "robot_102",
            Model: "AMB-150",
            Dispatchable: true,
            UndispatchableReason: null,
            CurrentMap: runtimeRouteMap,
            ActiveOrderId: "ORD-MULTI-MAP-01",
            ActiveOrderState: "RUNNING",
            Online: true);

        var client = new StubRdsReadClient([robot]);
        var store = new StubRdsIntegrationStore();
        var service = new RdsIntegrationService(client, store, TimeProvider.System);

        var adminRobots = await service.ListRobotsForAdminAsync(Guid.NewGuid());

        var record = Assert.Single(adminRobots);
        Assert.Equal(runtimeRouteMap, record.CurrentMap);
        Assert.NotEqual(preDispatchMap, record.CurrentMap);
        Assert.Equal("RUNNING", record.Execution);
        Assert.Equal("DISPATCHABLE", record.Dispatch);
        Assert.Equal("ONLINE", record.Connectivity);
    }

    [Fact]
    public async Task Online_and_undispatchable_is_representable_across_orthogonal_dimensions()
    {
        var robot = new RdsRobotStateSnapshot(
            RobotId: "robot_103",
            Model: "AMB-150",
            Dispatchable: false,
            UndispatchableReason: "low_battery",
            BatteryLevel: 0.12,
            Online: true);

        var client = new StubRdsReadClient([robot]);
        var store = new StubRdsIntegrationStore();
        var service = new RdsIntegrationService(client, store, TimeProvider.System);

        var adminRobots = await service.ListRobotsForAdminAsync(Guid.NewGuid());

        var record = Assert.Single(adminRobots);
        Assert.Equal("ONLINE", record.Connectivity);
        Assert.Equal("UNDISPATCHABLE", record.Dispatch);
        Assert.Equal("low_battery", record.UndispatchableReason);
        Assert.Equal(0.12, record.BatteryLevel);
    }

    [Fact]
    public async Task Online_and_blocked_and_running_is_representable_across_orthogonal_dimensions()
    {
        var robot = new RdsRobotStateSnapshot(
            RobotId: "robot_104",
            Model: "AMB-150",
            Dispatchable: true,
            UndispatchableReason: null,
            Blocked: true,
            ActiveOrderId: "ORD-BLOCKED-01",
            ActiveOrderState: "RUNNING",
            Online: true);

        var client = new StubRdsReadClient([robot]);
        var store = new StubRdsIntegrationStore();
        var service = new RdsIntegrationService(client, store, TimeProvider.System);

        var adminRobots = await service.ListRobotsForAdminAsync(Guid.NewGuid());

        var record = Assert.Single(adminRobots);
        Assert.Equal("ONLINE", record.Connectivity);
        Assert.Equal("BLOCKED", record.Motion);
        Assert.Equal("RUNNING", record.Execution);
        Assert.Equal("DISPATCHABLE", record.Dispatch);
    }

    // ---------------------------------------------------------------------
    // Admin lock correlation must fail CLOSED and must reuse the same
    // RdsCoreRobotLockInfo.CorrelatesWith contract the execution recovery path
    // uses. `locked=true` alone, a historical MAP_PREP_LOCK_ACQUIRED audit row
    // alone, a missing fingerprint, an incomplete fingerprint or a different
    // fingerprint may NEVER be labelled TKG_CORRELATED.
    // ---------------------------------------------------------------------

    private static RdsRobotStateSnapshot LockedRobot(
        string robotId,
        RdsCoreRobotLockInfo? lockInfo,
        RdsRobotIdentity? identity = null) =>
        new(
            RobotId: robotId,
            Model: "AMB-150",
            Dispatchable: false,
            UndispatchableReason: "unlock",
            LockInfo: lockInfo,
            Online: true,
            Identity: identity);

    private static async Task<RdsRobotAdminRecord> ProjectAsync(
        RdsRobotStateSnapshot robot,
        params RdsActiveLockFingerprint[] activeLocks)
    {
        var service = new RdsIntegrationService(
            new StubRdsReadClient([robot]),
            new StubRdsIntegrationStore(activeTkgLocks: activeLocks),
            TimeProvider.System);
        var records = await service.ListRobotsForAdminAsync(Guid.NewGuid());
        return Assert.Single(records);
    }

    [Fact]
    public async Task Case_A_full_matching_fingerprint_is_labeled_tkg_correlated()
    {
        var fingerprint = new RdsCoreRobotLockInfo(true, "10.0.0.5", "TKG_LOCK_1", 8088, "1788522696", 1, null);
        var robot = LockedRobot("robot_106", fingerprint);
        var captured = new RdsActiveLockFingerprint("robot_106", "26.7.2026_map_optimak", fingerprint);

        var record = await ProjectAsync(robot, captured);

        Assert.True(record.IsLocked);
        Assert.Equal("TKG_CORRELATED", record.Control);
    }

    [Fact]
    public async Task Case_B_different_nickname_is_not_tkg_correlated()
    {
        var captured = new RdsCoreRobotLockInfo(true, null, "TKG_LOCK_1", null, null, null, null);
        var current = new RdsCoreRobotLockInfo(true, null, "ROBOSHOP_EXTERNAL_LOCK", null, null, null, null);
        var robot = LockedRobot("robot_105", current);

        var record = await ProjectAsync(robot, new RdsActiveLockFingerprint("robot_105", "map", captured));

        Assert.NotEqual("TKG_CORRELATED", record.Control);
        Assert.Equal("EXTERNAL_OR_OTHER", record.Control);
    }

    [Fact]
    public async Task Case_C_captured_fingerprint_missing_is_not_tkg_correlated()
    {
        // Captured lock carries no usable fingerprint at all.
        var captured = new RdsCoreRobotLockInfo(true, null, null, null, null, null, null);
        var current = new RdsCoreRobotLockInfo(true, null, "ROBOSHOP_EXTERNAL_LOCK", null, null, null, null);
        var robot = LockedRobot("robot_107", current);

        var record = await ProjectAsync(robot, new RdsActiveLockFingerprint("robot_107", "map", captured));

        Assert.Equal("EXTERNAL_OR_OTHER", record.Control);
    }

    [Fact]
    public async Task Case_D_current_fingerprint_missing_is_not_tkg_correlated()
    {
        var captured = new RdsCoreRobotLockInfo(true, null, "TKG_LOCK_1", null, null, null, null);
        var current = new RdsCoreRobotLockInfo(true, null, null, null, null, null, null);
        var robot = LockedRobot("robot_108", current);

        var record = await ProjectAsync(robot, new RdsActiveLockFingerprint("robot_108", "map", captured));

        Assert.Equal("EXTERNAL_OR_OTHER", record.Control);
    }

    [Fact]
    public async Task Case_E_stale_unreleased_audit_with_external_lock_is_not_tkg_correlated()
    {
        // The crash that leaves a lock unreleased is the SAME path that fails to
        // capture a fingerprint, so the stale record carries CapturedLockInfo=null.
        // Before the fix this matched on vehicle id alone and mislabelled a
        // RoboShopPro lock as TKG-owned.
        var staleAuditRow = new RdsActiveLockFingerprint("robot_109", "26.7.2026_map_optimak", CapturedLockInfo: null);
        var externalLock = new RdsCoreRobotLockInfo(true, "192.168.40.9", "ROBOSHOP_PRO", 9000, "1788600000", 1, null);
        var robot = LockedRobot("robot_109", externalLock);

        var record = await ProjectAsync(robot, staleAuditRow);

        Assert.True(record.IsLocked);
        Assert.NotEqual("TKG_CORRELATED", record.Control);
        Assert.Equal("EXTERNAL_OR_OTHER", record.Control);
    }

    [Fact]
    public async Task Case_F_correlation_works_when_uuid_differs_from_vehicle_id()
    {
        var fingerprint = new RdsCoreRobotLockInfo(true, "10.0.0.5", "TKG_LOCK_1", 8088, "1788522696", 1, null);
        var identity = new RdsRobotIdentity(Uuid: "ROBOT-INTERNAL-123", VehicleId: "AGV-01");
        var robot = LockedRobot("AGV-01", fingerprint, identity);

        // The audit row recorded the dispatch vehicle identifier...
        var byVehicle = await ProjectAsync(robot, new RdsActiveLockFingerprint("AGV-01", "map", fingerprint));
        Assert.Equal("TKG_CORRELATED", byVehicle.Control);

        // ...and an older row may have recorded the internal uuid instead.
        var byUuid = await ProjectAsync(robot, new RdsActiveLockFingerprint("ROBOT-INTERNAL-123", "map", fingerprint));
        Assert.Equal("TKG_CORRELATED", byUuid.Control);

        // A different robot entirely must never correlate.
        var unrelated = await ProjectAsync(robot, new RdsActiveLockFingerprint("AGV-99", "map", fingerprint));
        Assert.Equal("EXTERNAL_OR_OTHER", unrelated.Control);

        Assert.Equal("ROBOT-INTERNAL-123", byVehicle.Uuid);
        Assert.Equal("AGV-01", byVehicle.VehicleId);
    }

    [Fact]
    public async Task Case_G_absent_lock_telemetry_is_unknown_not_unlocked()
    {
        var robot = LockedRobot("robot_110", lockInfo: null);

        var record = await ProjectAsync(robot);

        Assert.Null(record.IsLocked);
        Assert.Equal("UNKNOWN", record.Control);
        Assert.NotEqual("UNLOCKED", record.Control);
    }

    [Fact]
    public async Task Observed_unlocked_robot_is_reported_unlocked()
    {
        var robot = LockedRobot("robot_111", new RdsCoreRobotLockInfo(false));

        var record = await ProjectAsync(robot);

        Assert.False(record.IsLocked);
        Assert.Equal("UNLOCKED", record.Control);
    }

    // ---------------------------------------------------------------------
    // Absence of evidence is never positive or negative evidence.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Missing_connectivity_dispatch_and_battery_telemetry_stay_unknown()
    {
        var robot = new RdsRobotStateSnapshot(
            RobotId: "robot_112",
            Model: "AMB-150",
            Dispatchable: null,
            UndispatchableReason: null,
            BatteryLevel: null,
            Online: null);

        var record = await ProjectAsync(robot);

        Assert.Equal("UNKNOWN", record.Connectivity);
        Assert.Equal("UNKNOWN", record.Dispatch);
        Assert.Equal("UNKNOWN", record.Motion);
        Assert.Null(record.BatteryLevel);
    }

    [Fact]
    public async Task Notice_alarms_are_counted_separately_from_errors()
    {
        var robot = LockedRobot("robot_113", new RdsCoreRobotLockInfo(false));
        var alarms = new List<RdsRobotAlarmSnapshot>
        {
            new("robot_113", "notice", "80004", "no joining order", null, 1),
            new("robot_113", "notice", "80005", "another notice", null, 1),
            new("robot_113", "warning", "W1", "a warning", null, 1),
            new("robot_113", "error", "E1", "a real error", null, 1)
        };

        var service = new RdsIntegrationService(
            new StubRdsReadClient([robot], alarms),
            new StubRdsIntegrationStore(),
            TimeProvider.System);
        var record = Assert.Single(await service.ListRobotsForAdminAsync(Guid.NewGuid()));

        Assert.Equal(2, record.Alarms.Notice);
        Assert.Equal(1, record.Alarms.Warning);
        Assert.Equal(1, record.Alarms.Error);
        Assert.Equal(0, record.Alarms.Fatal);
        Assert.Equal(4, record.Alarms.Total);
        // Only fatal + error justify a danger indicator.
        Assert.Equal(1, record.Alarms.Critical);
    }

    private sealed class StubRdsReadClient(
        IReadOnlyList<RdsRobotStateSnapshot> robots,
        IReadOnlyList<RdsRobotAlarmSnapshot>? alarms = null) : IRdsReadClient
    {
        public bool Enabled => true;
        public string? SafeBaseAddress => "http://127.0.0.1:8080";
        public DateTimeOffset? LastSuccessfulRead => DateTimeOffset.UtcNow;

        public Task<RdsReadResult<IReadOnlyList<RdsWorkSiteSnapshot>>> GetWorkSitesAsync(Guid correlationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RdsReadResult<IReadOnlyList<RdsWorkSiteSnapshot>>([], 200, "Success", "/api/work-sites/sites", correlationId, DateTimeOffset.UtcNow));

        public Task<RdsReadResult<RdsWindTaskPage>> QueryWindTasksAsync(RdsWindTaskQuery query, Guid correlationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RdsReadResult<RdsWindTaskPage>(new(0, 1, 20, 0, []), 200, "Success", "/api/queryWindTask", correlationId, DateTimeOffset.UtcNow));

        public Task<RdsReadResult<RdsRobotAlarmReport>> GetRobotAlarmsAsync(Guid correlationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RdsReadResult<RdsRobotAlarmReport>(new(alarms ?? [], robots), 200, "Success", "/api/agv-report/core", correlationId, DateTimeOffset.UtcNow));
    }

    private sealed class StubRdsIntegrationStore(IReadOnlyList<RdsActiveLockFingerprint>? activeTkgLocks = null) : IRdsIntegrationStore
    {
        public Task<IReadOnlyList<RdsNavigationProfileRecord>> ListNavigationProfilesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RdsNavigationProfileRecord>>([]);

        public Task<(int Created, int Updated)> SyncWorkSitesAsync(Guid navigationProfileId, IReadOnlyList<RdsWorkSiteSnapshot> workSites, DateTimeOffset syncedAt, CancellationToken cancellationToken = default) =>
            Task.FromResult((0, 0));

        public Task<IReadOnlyList<RdsMirroredWorkSiteRecord>> ListWorkSitesAsync(RdsWorkSiteFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RdsMirroredWorkSiteRecord>>([]);

        public Task<IReadOnlyList<RdsSlotMappingRecord>> ListSlotMappingsAsync(Guid? layoutId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RdsSlotMappingRecord>>([]);

        public Task<RdsSlotMappingRecord> SetSlotMappingAsync(Guid slotId, Guid rdsWorkSiteId, AdminMutationContext context, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task RemoveSlotMappingAsync(Guid slotId, AdminMutationContext context, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<RdsActiveLockFingerprint>> GetActiveTkgLockFingerprintsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(activeTkgLocks ?? []);
    }
}
