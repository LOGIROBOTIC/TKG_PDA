using System.Text.Json;
using TkgPda.Application;
using TkgPda.Domain;
using Xunit;

namespace TkgPda.Application.Tests;

public sealed class RdsExecutionCoordinatorNativeOrderTests
{
    private static readonly Guid RequestId = Guid.Parse("44444444-5555-6666-7777-888888888888");

    [Fact]
    public async Task WaitingPreparation_readiness_state_never_dispatches_native_order()
    {
        var fixture = Fixture(readinessState: TransportReadinessState.WaitingPreparation);
        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("READINESS_WAITING_PREPARATION", result.Code);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
        Assert.Equal(0, fixture.ExecutionClient.CreateCount);
        Assert.Empty(fixture.WorkflowStore.Transitions);
    }

    [Fact]
    public async Task Duplicate_worker_attempt_aborts_when_lease_cannot_be_acquired()
    {
        var fixture = Fixture();
        fixture.Store.LeaseAvailable = false;

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.Null(result);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
        Assert.Equal(0, fixture.ExecutionClient.CreateCount);
    }

    [Fact]
    public async Task Native_dispatch_uses_deterministic_order_and_block_identities()
    {
        var fixture = Fixture();
        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.True(result.Dispatched);
        Assert.Equal(1, fixture.NativeClient.SetOrderCount);
        var order = fixture.NativeClient.LastOrder;
        Assert.NotNull(order);
        Assert.Equal(RequestId.ToString("D"), order.Id);
        Assert.Equal($"{RequestId:D}:pickup", order.Blocks[0].BlockId);
        Assert.Equal($"{RequestId:D}:dropoff", order.Blocks[1].BlockId);
        Assert.Contains(TransportRequestStatus.SentToRds, fixture.WorkflowStore.Transitions);
    }

    [Fact]
    public async Task Ambiguous_setOrder_network_timeout_recovers_via_details_query()
    {
        var fixture = Fixture();
        fixture.NativeClient.SetOrderError = new RdsCoreNativeOrderException("TIMEOUT", "Ambiguous network timeout");
        fixture.NativeClient.DetailsState = RdsCoreKnownOrderState.Running;

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.True(result.Dispatched);
        Assert.Contains(TransportRequestStatus.SentToRds, fixture.WorkflowStore.Transitions);
        Assert.Contains(TransportRequestStatus.Running, fixture.WorkflowStore.Transitions);
    }

    [Fact]
    public async Task Finished_native_order_reconciles_to_Completed()
    {
        var fixture = Fixture(activeTaskStatus: TransportRequestStatus.SentToRds);
        fixture.NativeClient.DetailsState = RdsCoreKnownOrderState.Finished;

        await fixture.Coordinator.ReconcileAsync(10, CancellationToken.None);

        Assert.Contains(TransportRequestStatus.Completed, fixture.WorkflowStore.Transitions);
    }

    [Fact]
    public async Task Stopped_native_order_reconciles_to_Cancelled_when_cancellation_requested()
    {
        var fixture = Fixture(activeTaskStatus: TransportRequestStatus.Running, cancellationRequested: true);
        fixture.NativeClient.DetailsState = RdsCoreKnownOrderState.Stopped;

        await fixture.Coordinator.ReconcileAsync(10, CancellationToken.None);

        Assert.Contains(fixture.WorkflowStore.Outcomes, o => o.Status == TransportRequestStatus.Cancelled);
    }

    [Fact]
    public async Task Failed_native_order_reconciles_to_Failed_safely()
    {
        var fixture = Fixture(activeTaskStatus: TransportRequestStatus.Running);
        fixture.NativeClient.DetailsState = RdsCoreKnownOrderState.Failed;

        await fixture.Coordinator.ReconcileAsync(10, CancellationToken.None);

        Assert.Contains(fixture.WorkflowStore.Outcomes, o => o.Status == TransportRequestStatus.Failed);
    }

    [Fact]
    public async Task Unknown_RDS_state_does_not_mutate_request()
    {
        var fixture = Fixture(activeTaskStatus: TransportRequestStatus.SentToRds);
        fixture.NativeClient.DetailsState = RdsCoreKnownOrderState.Unknown;

        await fixture.Coordinator.ReconcileAsync(10, CancellationToken.None);

        Assert.Empty(fixture.WorkflowStore.Transitions);
        Assert.Empty(fixture.WorkflowStore.Outcomes);
    }

    [Fact]
    public async Task Native_and_Wind_mode_are_mutually_exclusive()
    {
        // Native mode: executionClient (WindTask) must NOT be invoked
        var nativeFixture = Fixture(mode: RdsExecutionMode.RdsCoreNativeOrder);
        await nativeFixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());
        Assert.Equal(1, nativeFixture.NativeClient.SetOrderCount);
        Assert.Equal(0, nativeFixture.ExecutionClient.CreateCount);

        // WindTask mode: nativeClient must NOT be invoked
        var windFixture = Fixture(mode: RdsExecutionMode.WindTask);
        await windFixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());
        Assert.Equal(0, windFixture.NativeClient.SetOrderCount);
        Assert.Equal(1, windFixture.ExecutionClient.CreateCount);
    }

    [Fact]
    public async Task Repeated_cancel_is_idempotent()
    {
        var fixture = Fixture(requestStatus: TransportRequestStatus.Cancelled);
        var first = await fixture.Coordinator.CancelAsync(RequestId, null, Guid.NewGuid());
        Assert.True(first.WasIdempotentReplay);

        var second = await fixture.Coordinator.CancelAsync(RequestId, null, Guid.NewGuid());
        Assert.True(second.WasIdempotentReplay);
        Assert.Equal(0, fixture.NativeClient.TerminateCount);
    }

    [Fact]
    public async Task Same_map_bypasses_switch_map_and_dispatches_transport()
    {
        var fixture = Fixture();
        fixture.NativeClient.CurrentRobotMap = "26.7.2026_map_optimak";
        fixture.Store.NavigationProfileSummary = new(
            Guid.NewGuid(), "DEV-MAP-A", "DEV-MAP-A", "ACTIVE", "26.7.2026_map_optimak", "SM11", "Sfl-Cbd15", JsonDocument.Parse("{}").RootElement);

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.True(result.Dispatched);
        Assert.Equal(0, fixture.NativeClient.SwitchMapCount);
        Assert.Equal(1, fixture.NativeClient.SetOrderCount);
        Assert.Equal(RequestId.ToString("D"), fixture.NativeClient.LastOrder?.Id);
    }

    [Fact]
    public async Task Different_map_orchestrates_navigation_to_transition_station_locks_switches_unlocks_and_dispatches_transport()
    {
        var fixture = Fixture();
        fixture.NativeClient.CurrentRobotMap = "26.7.2026_map_optimak";
        fixture.NativeClient.CurrentRobotStation = "AP21";
        fixture.Store.NavigationProfileSummary = new(
            Guid.NewGuid(), "DEV-MAP-B", "DEV-MAP-B", "ACTIVE", "26.7.2026_map_optimak_1", "SM11", "Sfl-Cbd15", JsonDocument.Parse("{}").RootElement);

        // Cycle 1: Robot is at AP21, not at transition station SM11 -> dispatches transition navigation order
        var result1 = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result1);
        Assert.True(result1.Dispatched);
        Assert.Equal("MAP_PREP_DISPATCHED", result1.Code);
        Assert.Equal(1, fixture.NativeClient.SetOrderCount);
        var prepOrder = fixture.NativeClient.Orders[0];
        Assert.Equal($"{RequestId:D}:map-prep:26.7.2026_map_optimak_1", prepOrder.Id);
        Assert.Equal("SM11", prepOrder.Blocks[0].Location);
        Assert.Equal(0, fixture.NativeClient.SwitchMapCount);

        // Robot arrives at SM11, transition navigation finished
        fixture.NativeClient.CurrentRobotStation = "SM11";
        fixture.NativeClient.TransitionOrderState = RdsCoreKnownOrderState.Finished;

        // Cycle 2: Robot at SM11 -> locks, switches map, unlocks, verifies, dispatches main transport order
        var result2 = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result2);
        Assert.True(result2.Dispatched);
        Assert.Equal(2, fixture.NativeClient.SetOrderCount);
        Assert.Equal(1, fixture.NativeClient.LockCount);
        Assert.Equal(1, fixture.NativeClient.SwitchMapCount);
        Assert.Equal("26.7.2026_map_optimak_1", fixture.NativeClient.LastSwitchedMap);
        Assert.Equal(1, fixture.NativeClient.UnlockCount);
        var mainOrder = fixture.NativeClient.Orders[1];
        Assert.Equal(RequestId.ToString("D"), mainOrder.Id);
    }

    [Fact]
    public async Task Different_map_when_already_at_transition_station_skips_preliminary_navigation()
    {
        var fixture = Fixture();
        fixture.NativeClient.CurrentRobotMap = "26.7.2026_map_optimak";
        fixture.NativeClient.CurrentRobotStation = "SM11";
        fixture.Store.NavigationProfileSummary = new(
            Guid.NewGuid(), "DEV-MAP-B", "DEV-MAP-B", "ACTIVE", "26.7.2026_map_optimak_1", "SM11", "Sfl-Cbd15", JsonDocument.Parse("{}").RootElement);

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.True(result.Dispatched);
        Assert.Equal(1, fixture.NativeClient.SetOrderCount);
        Assert.Equal(RequestId.ToString("D"), fixture.NativeClient.Orders[0].Id);
        Assert.Equal(1, fixture.NativeClient.LockCount);
        Assert.Equal(1, fixture.NativeClient.SwitchMapCount);
        Assert.Equal(1, fixture.NativeClient.UnlockCount);
    }

    [Fact]
    public async Task Fail_closed_when_NavigationProfile_unresolved()
    {
        var fixture = Fixture();
        fixture.Store.NavigationProfileSummary = null;

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("NAVIGATION_PROFILE_UNRESOLVED", result.Code);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
    }

    [Fact]
    public async Task Fail_closed_when_required_map_unresolved()
    {
        var fixture = Fixture();
        fixture.Store.NavigationProfileSummary = fixture.Store.NavigationProfileSummary! with { RobotMapName = "" };

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("REQUIRED_MAP_UNRESOLVED", result.Code);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
    }

    [Fact]
    public async Task Fail_closed_when_vehicle_unresolved()
    {
        var fixture = Fixture();
        fixture.Store.NavigationProfileSummary = fixture.Store.NavigationProfileSummary! with { Vehicle = "" };

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("VEHICLE_UNRESOLVED", result.Code);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
    }

    [Fact]
    public async Task Fail_closed_when_robot_offline()
    {
        var fixture = Fixture();
        fixture.NativeClient.RobotConnectionStatus = 0;

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("ROBOT_OFFLINE", result.Code);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
    }

    [Fact]
    public async Task Fail_closed_when_robot_not_dispatchable()
    {
        var fixture = Fixture();
        fixture.NativeClient.RobotDispatchable = false;

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("ROBOT_NOT_DISPATCHABLE", result.Code);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
    }

    [Fact]
    public async Task Fail_closed_when_robot_has_blocking_alarm()
    {
        var fixture = Fixture();
        fixture.NativeClient.RobotIsError = true;

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("ROBOT_HAS_BLOCKING_ALARM", result.Code);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
    }

    [Fact]
    public async Task Fail_closed_when_robot_has_incompatible_order()
    {
        var fixture = Fixture();
        fixture.NativeClient.RobotCurrentOrderId = "other-incompatible-order";

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("ROBOT_BUSY_INCOMPATIBLE_ORDER", result.Code);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
    }

    [Fact]
    public async Task Fail_closed_when_switch_transition_station_unresolved()
    {
        var fixture = Fixture();
        fixture.NativeClient.CurrentRobotMap = "26.7.2026_map_optimak";
        fixture.Store.NavigationProfileSummary = new(
            Guid.NewGuid(), "DEV-MAP-B", "DEV-MAP-B", "ACTIVE", "26.7.2026_map_optimak_1", null, "Sfl-Cbd15", JsonDocument.Parse("{}").RootElement);

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("SWITCH_TRANSITION_UNRESOLVED", result.Code);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
    }

    [Fact]
    public async Task Fail_closed_when_switchMap_fails()
    {
        var fixture = Fixture();
        fixture.NativeClient.CurrentRobotMap = "26.7.2026_map_optimak";
        fixture.NativeClient.CurrentRobotStation = "SM11";
        fixture.Store.NavigationProfileSummary = new(
            Guid.NewGuid(), "DEV-MAP-B", "DEV-MAP-B", "ACTIVE", "26.7.2026_map_optimak_1", "SM11", "Sfl-Cbd15", JsonDocument.Parse("{}").RootElement);
        fixture.NativeClient.SwitchMapError = new RdsCoreNativeOrderException("SWITCH_ERR", "SwitchMap rejected by RDS");

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("SWITCHMAP_FAILED", result.Code);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
    }

    [Fact]
    public async Task Fail_closed_when_switchMap_verification_fails()
    {
        var fixture = Fixture();
        fixture.NativeClient.CurrentRobotMap = "26.7.2026_map_optimak";
        fixture.NativeClient.CurrentRobotStation = "SM11";
        fixture.Store.NavigationProfileSummary = new(
            Guid.NewGuid(), "DEV-MAP-B", "DEV-MAP-B", "ACTIVE", "26.7.2026_map_optimak_1", "SM11", "Sfl-Cbd15", JsonDocument.Parse("{}").RootElement);
        fixture.NativeClient.KeepOldMapOnSwitch = true;

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("SWITCHMAP_VERIFICATION_FAILED", result.Code);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
    }

    [Fact]
    public async Task Fail_closed_when_post_switch_alarm_detected()
    {
        var fixture = Fixture();
        fixture.NativeClient.CurrentRobotMap = "26.7.2026_map_optimak";
        fixture.NativeClient.CurrentRobotStation = "SM11";
        fixture.Store.NavigationProfileSummary = new(
            Guid.NewGuid(), "DEV-MAP-B", "DEV-MAP-B", "ACTIVE", "26.7.2026_map_optimak_1", "SM11", "Sfl-Cbd15", JsonDocument.Parse("{}").RootElement);
        fixture.NativeClient.AlarmAfterSwitch = true;

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("POST_SWITCH_ALARM", result.Code);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
    }

    [Fact]
    public async Task Restart_recovery_when_map_already_switched_resumes_without_duplicate_switch()
    {
        var fixture = Fixture();
        // Previous run already switched robot to Map B
        fixture.NativeClient.CurrentRobotMap = "26.7.2026_map_optimak_1";
        fixture.Store.NavigationProfileSummary = new(
            Guid.NewGuid(), "DEV-MAP-B", "DEV-MAP-B", "ACTIVE", "26.7.2026_map_optimak_1", "SM11", "Sfl-Cbd15", JsonDocument.Parse("{}").RootElement);

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.True(result.Dispatched);
        Assert.Equal(0, fixture.NativeClient.SwitchMapCount);
        Assert.Equal(1, fixture.NativeClient.SetOrderCount);
        Assert.Equal(RequestId.ToString("D"), fixture.NativeClient.LastOrder?.Id);
    }

    [Fact]
    public async Task ExecutionMode_missing_fails_closed_with_EXECUTION_MODE_UNRESOLVED()
    {
        var fixture = Fixture();
        fixture.Store.Context = Context(TransportReadinessState.Ready, executionMode: null);

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("EXECUTION_MODE_UNRESOLVED", result.Code);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
        Assert.Equal(0, fixture.ExecutionClient.CreateCount);
    }

    [Fact]
    public async Task ExecutionMode_invalid_fails_closed_with_EXECUTION_MODE_INVALID()
    {
        var fixture = Fixture();
        fixture.Store.Context = Context(TransportReadinessState.Ready, executionMode: "INVALID_MODE");

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("EXECUTION_MODE_INVALID", result.Code);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
        Assert.Equal(0, fixture.ExecutionClient.CreateCount);
    }

    [Fact]
    public async Task Unmapped_variant_fails_closed_with_NAVIGATION_PROFILE_UNRESOLVED()
    {
        var fixture = Fixture();
        fixture.Store.NavigationProfileSummary = null;

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("NAVIGATION_PROFILE_UNRESOLVED", result.Code);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
        Assert.Equal(0, fixture.NativeClient.SwitchMapCount);
    }

    [Fact]
    public async Task Wrong_machine_or_different_variant_never_resolves_profile()
    {
        var fixture = Fixture();
        var expectedVariant = Guid.NewGuid();
        var wrongVariant = Guid.NewGuid();
        fixture.Store.ExpectedVariantId = expectedVariant;
        fixture.Store.Context = Context(TransportReadinessState.Ready, variantId: wrongVariant);

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("NAVIGATION_PROFILE_UNRESOLVED", result.Code);
    }

    [Fact]
    public async Task Switch_transition_unconfirmed_when_robot_station_does_not_match_transition_station()
    {
        var fixture = Fixture();
        fixture.NativeClient.CurrentRobotMap = "26.7.2026_map_optimak";
        fixture.NativeClient.CurrentRobotStation = "AP21";
        fixture.Store.NavigationProfileSummary = new(
            Guid.NewGuid(), "DEV-MAP-B", "DEV-MAP-B", "ACTIVE", "26.7.2026_map_optimak_1", "SM11", "Sfl-Cbd15", JsonDocument.Parse("{}").RootElement);

        // Prep order finished, but robot telemetry reports current_station still at AP21 (not SM11)
        fixture.NativeClient.TransitionOrderState = RdsCoreKnownOrderState.Finished;
        fixture.NativeClient.CurrentRobotStation = "AP21";

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("SWITCH_TRANSITION_NOT_CONFIRMED", result.Code);
        Assert.Equal(0, fixture.NativeClient.LockCount);
        Assert.Equal(0, fixture.NativeClient.SwitchMapCount);
    }

    [Fact]
    public async Task Switch_transition_unconfirmed_when_robot_has_alarm_at_transition_station()
    {
        var fixture = Fixture();
        fixture.NativeClient.CurrentRobotMap = "26.7.2026_map_optimak";
        fixture.NativeClient.CurrentRobotStation = "SM11";
        fixture.NativeClient.TransitionOrderState = RdsCoreKnownOrderState.Finished;
        fixture.NativeClient.RobotIsError = true;
        fixture.NativeClient.RobotAlarms.Add("BLOCKING_SENSOR_FAULT");
        fixture.Store.NavigationProfileSummary = new(
            Guid.NewGuid(), "DEV-MAP-B", "DEV-MAP-B", "ACTIVE", "26.7.2026_map_optimak_1", "SM11", "Sfl-Cbd15", JsonDocument.Parse("{}").RootElement);

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("ROBOT_HAS_BLOCKING_ALARM", result.Code);
        Assert.Equal(0, fixture.NativeClient.LockCount);
        Assert.Equal(0, fixture.NativeClient.SwitchMapCount);
    }

    [Fact]
    public async Task Crash_recovery_scenario_A_lock_acquired_before_switchMap_crashed_fails_closed()
    {
        var fixture = Fixture();
        fixture.NativeClient.CurrentRobotMap = "26.7.2026_map_optimak";
        fixture.NativeClient.RobotDispatchable = true;
        fixture.Store.HasUnreleasedLock = true;
        fixture.Store.NavigationProfileSummary = new(
            Guid.NewGuid(), "DEV-MAP-B", "DEV-MAP-B", "ACTIVE", "26.7.2026_map_optimak_1", "SM11", "Sfl-Cbd15", JsonDocument.Parse("{}").RootElement);

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("MAP_SWITCH_LOCK_RECOVERY_REQUIRED", result.Code);
        Assert.Equal(0, fixture.NativeClient.SwitchMapCount);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
    }

    [Fact]
    public async Task Crash_race_A_TKG_lock_fingerprint_matches_after_restart_safe_recovery_behavior()
    {
        var fixture = Fixture();
        var lockInfo = new RdsCoreRobotLockInfo(true, "10.0.0.1", "TKG-NATIVE", 5000, "1634023715", 2, "ok");
        fixture.NativeClient.CurrentRobotMap = "26.7.2026_map_optimak_1";
        fixture.NativeClient.RobotDispatchable = false;
        fixture.NativeClient.RobotLockInfo = lockInfo;
        fixture.Store.MapSwitchLockSnapshot = new RdsMapSwitchLockSnapshot(true, "Sfl-Cbd15", "26.7.2026_map_optimak_1", lockInfo);
        fixture.Store.NavigationProfileSummary = new(
            Guid.NewGuid(), "DEV-MAP-B", "DEV-MAP-B", "ACTIVE", "26.7.2026_map_optimak_1", "SM11", "Sfl-Cbd15", JsonDocument.Parse("{}").RootElement);

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.True(result.Dispatched);
        Assert.Equal(1, fixture.NativeClient.UnlockCount);
        Assert.Equal(1, fixture.NativeClient.SetOrderCount);
    }

    [Fact]
    public async Task Crash_race_B_TKG_historical_unreleased_audit_exists_but_current_lock_info_differs_never_unlocks()
    {
        var fixture = Fixture();
        var tkgLock = new RdsCoreRobotLockInfo(true, "10.0.0.1", "TKG-NATIVE", 5000, "1634023715", 2, "ok");
        var externalLock = new RdsCoreRobotLockInfo(true, "10.0.0.99", "EXT-ENG", 5500, "1634029999", 2, "commissioning");
        fixture.NativeClient.CurrentRobotMap = "26.7.2026_map_optimak_1";
        fixture.NativeClient.RobotDispatchable = false;
        fixture.NativeClient.RobotLockInfo = externalLock;
        fixture.Store.MapSwitchLockSnapshot = new RdsMapSwitchLockSnapshot(true, "Sfl-Cbd15", "26.7.2026_map_optimak_1", tkgLock);
        fixture.Store.NavigationProfileSummary = new(
            Guid.NewGuid(), "DEV-MAP-B", "DEV-MAP-B", "ACTIVE", "26.7.2026_map_optimak_1", "SM11", "Sfl-Cbd15", JsonDocument.Parse("{}").RootElement);

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("MAP_SWITCH_LOCK_RECOVERY_REQUIRED", result.Code);
        Assert.Equal(0, fixture.NativeClient.UnlockCount);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
    }

    [Fact]
    public async Task Crash_race_C_TKG_historical_unreleased_audit_exists_but_current_lock_info_unavailable_never_unlocks()
    {
        var fixture = Fixture();
        var tkgLock = new RdsCoreRobotLockInfo(true, "10.0.0.1", "TKG-NATIVE", 5000, "1634023715", 2, "ok");
        fixture.NativeClient.CurrentRobotMap = "26.7.2026_map_optimak_1";
        fixture.NativeClient.RobotDispatchable = false;
        fixture.NativeClient.RobotLockInfo = null;
        fixture.Store.MapSwitchLockSnapshot = new RdsMapSwitchLockSnapshot(true, "Sfl-Cbd15", "26.7.2026_map_optimak_1", tkgLock);
        fixture.Store.NavigationProfileSummary = new(
            Guid.NewGuid(), "DEV-MAP-B", "DEV-MAP-B", "ACTIVE", "26.7.2026_map_optimak_1", "SM11", "Sfl-Cbd15", JsonDocument.Parse("{}").RootElement);

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("MAP_SWITCH_LOCK_RECOVERY_REQUIRED", result.Code);
        Assert.Equal(0, fixture.NativeClient.UnlockCount);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
    }

    [Fact]
    public async Task Crash_race_D_unlock_succeeded_but_release_audit_lost_then_external_lock_acquired_never_unlocks_external_lock()
    {
        var fixture = Fixture();
        var oldTkgLock = new RdsCoreRobotLockInfo(true, "10.0.0.1", "TKG-NATIVE", 5000, "1634023715", 2, "ok");
        var externalLock = new RdsCoreRobotLockInfo(true, "192.168.1.88", "ENG-ROBOSHOP", 6000, "1634030000", 255, "tuning");
        fixture.NativeClient.CurrentRobotMap = "26.7.2026_map_optimak_1";
        fixture.NativeClient.RobotDispatchable = false;
        fixture.NativeClient.RobotLockInfo = externalLock;
        fixture.Store.MapSwitchLockSnapshot = new RdsMapSwitchLockSnapshot(true, "Sfl-Cbd15", "26.7.2026_map_optimak_1", oldTkgLock);
        fixture.Store.NavigationProfileSummary = new(
            Guid.NewGuid(), "DEV-MAP-B", "DEV-MAP-B", "ACTIVE", "26.7.2026_map_optimak_1", "SM11", "Sfl-Cbd15", JsonDocument.Parse("{}").RootElement);

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.False(result.Dispatched);
        Assert.Equal("MAP_SWITCH_LOCK_RECOVERY_REQUIRED", result.Code);
        Assert.Equal(0, fixture.NativeClient.UnlockCount);
        Assert.Equal(0, fixture.NativeClient.SetOrderCount);
    }

    [Fact]
    public async Task Crash_race_E_same_TKG_lock_still_exists_after_crash_and_target_map_already_switched_safe_correlated_recovery_unlock()
    {
        var fixture = Fixture();
        var tkgLock = new RdsCoreRobotLockInfo(true, "10.0.0.1", "TKG-NATIVE", 5000, "1634023715", 2, "ok");
        fixture.NativeClient.CurrentRobotMap = "26.7.2026_map_optimak_1";
        fixture.NativeClient.RobotDispatchable = false;
        fixture.NativeClient.RobotLockInfo = tkgLock;
        fixture.Store.MapSwitchLockSnapshot = new RdsMapSwitchLockSnapshot(true, "Sfl-Cbd15", "26.7.2026_map_optimak_1", tkgLock);
        fixture.Store.NavigationProfileSummary = new(
            Guid.NewGuid(), "DEV-MAP-B", "DEV-MAP-B", "ACTIVE", "26.7.2026_map_optimak_1", "SM11", "Sfl-Cbd15", JsonDocument.Parse("{}").RootElement);

        var result = await fixture.Coordinator.DispatchOneAsync(RequestId, Guid.NewGuid());

        Assert.NotNull(result);
        Assert.True(result.Dispatched);
        Assert.Equal(1, fixture.NativeClient.UnlockCount);
        Assert.Equal(1, fixture.NativeClient.SetOrderCount);
    }

    private static NativeTestFixture Fixture(
        TransportRequestStatus requestStatus = TransportRequestStatus.Queued,
        TransportReadinessState readinessState = TransportReadinessState.Ready,
        TransportRequestStatus? activeTaskStatus = null,
        bool cancellationRequested = false,
        RdsExecutionMode mode = RdsExecutionMode.RdsCoreNativeOrder)
    {
        var executionMode = mode == RdsExecutionMode.WindTask ? "WIND_TASK" : "RDSCORE_NATIVE_ORDER";
        var context = Context(readinessState: readinessState, executionMode: executionMode);
        var store = new FakeNativeStore
        {
            Context = context,
            RequestStatus = requestStatus,
            CancellationRequested = cancellationRequested
        };

        if (activeTaskStatus is { } activeStatus)
        {
            var proj = new RdsTaskProjection(
                RequestId, RequestId.ToString("D"), "TKG_TRANSPORT", 1000, null, null, null,
                JsonDocument.Parse("{}").RootElement, JsonDocument.Parse("{}").RootElement, JsonDocument.Parse("{}").RootElement,
                cancellationRequested, activeStatus);
            store.Active = [proj];
            store.TaskProjection = proj;
        }

        var workflowStore = new FakeNativeWorkflowStore(store.Request);
        var executionClient = new FakeNativeExecutionClient();
        var readClient = new FakeNativeReadClient();
        var reconciler = new FakeNativeWorkSiteReconciler();
        var nativeClient = new FakeNativeOrderClient(mode);
        var settings = new RdsExecutionSettings(true, TimeSpan.FromMinutes(5), "", "");

        var coordinator = new RdsExecutionCoordinator(
            store,
            executionClient,
            readClient,
            reconciler,
            new TransportWorkflowService(workflowStore),
            settings,
            TimeProvider.System,
            nativeOrderClient: nativeClient);

        return new(coordinator, store, workflowStore, executionClient, nativeClient);
    }

    private static RdsDispatchContext Context(
        TransportReadinessState readinessState,
        string? executionMode = "RDSCORE_NATIVE_ORDER",
        Guid? variantId = null,
        Guid? machineId = null) => new(
        new TransportRequestSnapshot(
            RequestId, 1, "test-key", TransportRequestStatus.Queued, TransportTaskType.BringEmpty,
            RequestOrigin.Api, Guid.NewGuid(), machineId ?? Guid.NewGuid(), variantId ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100,
            readinessState, null),
        "SH-01-A", "V1", "71550", "715A2", "KCK", Guid.NewGuid(),
        "TKG_TRANSPORT", 100,
        executionMode is not null
            ? JsonSerializer.SerializeToElement(new
            {
                executionMode = executionMode,
                rdsInputBindings = new[]
                {
                    new { param = "fromSite", source = "SOURCE_RDS_SITE_ID", required = true, value = (string?)null },
                    new { param = "toSite", source = "DESTINATION_RDS_SITE_ID", required = true, value = (string?)null }
                }
            })
            : JsonSerializer.SerializeToElement(new
            {
                rdsInputBindings = new[]
                {
                    new { param = "fromSite", source = "SOURCE_RDS_SITE_ID", required = true, value = (string?)null },
                    new { param = "toSite", source = "DESTINATION_RDS_SITE_ID", required = true, value = (string?)null }
                }
            }),
        new RdsDispatchEndpoint(Guid.NewGuid(), "SOURCE_STATION", "GRP", "STAGING", "SOURCE_STATION", 1, 0, 0, 0, 0, DateTimeOffset.UtcNow, true),
        new RdsDispatchEndpoint(Guid.NewGuid(), "DEST_STATION", "GRP", "STAGING", "DEST_STATION", 1, 0, 0, 0, 0, DateTimeOffset.UtcNow, true));

    private sealed record NativeTestFixture(
        RdsExecutionCoordinator Coordinator,
        FakeNativeStore Store,
        FakeNativeWorkflowStore WorkflowStore,
        FakeNativeExecutionClient ExecutionClient,
        FakeNativeOrderClient NativeClient);

    private sealed class FakeNativeExecutionLease : IRdsExecutionLease
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeNativeStore : IRdsExecutionStore
    {
        public RdsDispatchContext Context { get; set; } = Context(TransportReadinessState.Ready);
        public TransportRequestStatus RequestStatus { get; set; }
        public bool CancellationRequested { get; set; }
        public bool LeaseAvailable { get; set; } = true;
        public IReadOnlyList<RdsTaskProjection> Active { get; set; } = [];
        public RdsTaskProjection? TaskProjection { get; set; }
        public TransportRequestSnapshot Request => Context.Request with { Status = RequestStatus };
        public RdsNavigationProfileSummary? NavigationProfileSummary { get; set; } = new(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            "DEV-RDS-SIM-MAP-A",
            "DEV-RDS-SIM-MAP-A",
            "ACTIVE",
            "26.7.2026_map_optimak",
            "SM11",
            "Sfl-Cbd15",
            JsonDocument.Parse("{}").RootElement);

        public bool HasUnreleasedLock { get; set; } = false;
        public RdsMapSwitchLockSnapshot? MapSwitchLockSnapshot { get; set; }
        public Guid? ExpectedVariantId { get; set; }
        public Guid? ExpectedMachineId { get; set; }

        public Task<IReadOnlyList<Guid>> ListQueuedRequestIdsAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([RequestId]);
        public Task<IReadOnlyList<RdsTaskProjection>> ListActiveTasksAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult(Active);
        public Task<IReadOnlyList<Guid>> ListPendingWorkSiteRefreshRequestIdsAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<IRdsExecutionLease?> TryAcquireRequestLeaseAsync(Guid requestId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IRdsExecutionLease?>(LeaseAvailable ? new FakeNativeExecutionLease() : null);
        public Task<RdsDispatchContext?> GetDispatchContextAsync(Guid requestId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RdsDispatchContext?>(Context with { Request = Request });
        public Task<TransportRequestSnapshot?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TransportRequestSnapshot?>(Request);
        public Task<RdsTaskProjection?> FindTaskByRequestAsync(Guid requestId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TaskProjection);
        public Task<RdsTaskProjection?> FindTaskByRecordIdAsync(string taskRecordId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TaskProjection);
        public Task EnsureTaskRecordAsync(Guid requestId, string taskRecordId, string taskLabel, JsonElement requestPayload, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task RecordDispatchResponseAsync(Guid requestId, JsonElement responsePayload, DateTimeOffset syncedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task RecordTaskStatusAsync(Guid requestId, RdsWindTaskSnapshot task, DateTimeOffset syncedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task MarkCancellationRequestedAsync(Guid requestId, JsonElement responsePayload, DateTimeOffset requestedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task WriteExecutionAuditAsync(Guid requestId, string action, string code, string message, Guid correlationId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task WriteExecutionAuditAsync(Guid requestId, string action, string code, string message, Guid correlationId, object? extraDetails, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task<RdsMappedWorkSite?> FindMappedWorkSiteAsync(Guid slotId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RdsMappedWorkSite?>(null);
        public Task<RdsNavigationProfileSummary?> GetNavigationProfileSummaryAsync(Guid? profileId, Guid variantId, Guid machineId, CancellationToken cancellationToken = default)
        {
            if (ExpectedVariantId.HasValue && variantId != ExpectedVariantId.Value) return Task.FromResult<RdsNavigationProfileSummary?>(null);
            if (ExpectedMachineId.HasValue && machineId != ExpectedMachineId.Value) return Task.FromResult<RdsNavigationProfileSummary?>(null);
            return Task.FromResult(NavigationProfileSummary);
        }
        public Task<bool> HasUnreleasedMapSwitchLockAsync(Guid requestId, string vehicle, CancellationToken cancellationToken = default) =>
            Task.FromResult(HasUnreleasedLock || MapSwitchLockSnapshot?.HasUnreleasedLock == true);
        public Task<RdsMapSwitchLockSnapshot?> GetUnreleasedMapSwitchLockAsync(Guid requestId, string vehicle, CancellationToken cancellationToken = default) =>
            Task.FromResult(MapSwitchLockSnapshot ?? (HasUnreleasedLock ? new RdsMapSwitchLockSnapshot(true, vehicle, null, null) : null));
        public Task<IReadOnlyList<RdsTaskTemplateSummary>> ListTaskTemplatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RdsTaskTemplateSummary>>([]);
        public Task EnrichRemainingTimesAsync(IReadOnlyDictionary<string, double> agvRemainingTimes, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeNativeWorkflowStore(TransportRequestSnapshot initial) : ITransportWorkflowStore
    {
        public List<TransportRequestStatus> Transitions { get; } = [];
        public List<ExecutionOutcome> Outcomes { get; } = [];

        public Task<CreateTransportResult> CreateAndQueueAsync(CreateTransportRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TransportBatchResult> CreateBatchAndQueueAsync(CreateTransportBatchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CancelTransportResult> CancelAsync(Guid requestId, Guid? actorUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CancelTransportResult(initial with { Status = TransportRequestStatus.Cancelled }, initial.Status == TransportRequestStatus.Cancelled));
        public Task<TransportRequestSnapshot> MarkReadyAsync(Guid requestId, Guid? actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TransportRequestSnapshot> RecordExecutionTransitionAsync(Guid requestId, TransportRequestStatus nextStatus, CancellationToken cancellationToken = default)
        {
            Transitions.Add(nextStatus);
            return Task.FromResult(initial with { Status = nextStatus });
        }
        public Task<TransportRequestSnapshot> RecordExecutionOutcomeAsync(Guid requestId, ExecutionOutcome outcome, CancellationToken cancellationToken = default)
        {
            Outcomes.Add(outcome);
            return Task.FromResult(initial with { Status = outcome.Status });
        }
        public Task<LoadStateUpdateResult> UpdateLoadStateAsync(LoadStateUpdateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SwitchVariantAsync(VariantSelection selection, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeNativeExecutionClient : IRdsExecutionClient
    {
        public bool DispatchEnabled => true;
        public int CreateCount { get; private set; }
        public Task<RdsValidationResult> ValidateTransportAsync(RdsValidateTransportRequest request, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCreateTransportResult> CreateTransportAsync(RdsCreateTransportRequest request, Guid correlationId, CancellationToken cancellationToken = default)
        {
            CreateCount++;
            return Task.FromResult(new RdsCreateTransportResult(true, false, 200, "ok", JsonDocument.Parse("{}").RootElement));
        }
        public Task<RdsStopTaskResult> StopTaskAsync(string taskRecordId, Guid correlationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RdsStopTaskResult(200, "ok", JsonDocument.Parse("{}").RootElement));
        public Task<RdsContentUpdateResult> UpdateContentAsync(string siteId, string content, Guid correlationId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeNativeReadClient : IRdsReadClient
    {
        public bool Enabled => true;
        public string? SafeBaseAddress => "http://127.0.0.1:8080";
        public DateTimeOffset? LastSuccessfulRead => DateTimeOffset.UtcNow;
        public Task<RdsReadResult<IReadOnlyList<RdsWorkSiteSnapshot>>> GetWorkSitesAsync(Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsReadResult<RdsWindTaskPage>> QueryWindTasksAsync(RdsWindTaskQuery query, Guid correlationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RdsReadResult<RdsWindTaskPage>(new(1, 1, 100, 1, [new RdsWindTaskSnapshot(RequestId.ToString(), "TKG", 1000, null, null, JsonDocument.Parse("{}").RootElement, null)]), 200, "ok", "/api/queryWindTask", correlationId, DateTimeOffset.UtcNow));
        public Task<RdsReadResult<RdsRobotAlarmReport>> GetRobotAlarmsAsync(Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeNativeWorkSiteReconciler : IRdsWorkSiteReconciler
    {
        public Task<RdsWorkSiteSyncRecord> SyncWorkSitesAsync(Guid navigationProfileId, Guid correlationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RdsWorkSiteSyncRecord(navigationProfileId, 1, 0, 1, DateTimeOffset.UtcNow, 0, "ok"));
    }

    private sealed class FakeNativeOrderClient(RdsExecutionMode mode) : IRdsCoreNativeOrderClient
    {
        public bool MutationEnabled => true;
        public RdsExecutionMode Mode => mode;
        public int SetOrderCount { get; private set; }
        public int TerminateCount { get; private set; }
        public int LockCount { get; private set; }
        public int UnlockCount { get; private set; }
        public int SwitchMapCount { get; private set; }
        public string? LastSwitchedMap { get; private set; }
        public List<RdsCoreNormalOrder> Orders { get; } = [];
        public RdsCoreNormalOrder? LastOrder => Orders.LastOrDefault();
        public Exception? SetOrderError { get; set; }
        public Exception? LockError { get; set; }
        public Exception? UnlockError { get; set; }
        public Exception? SwitchMapError { get; set; }
        public Exception? RobotsStatusError { get; set; }

        public string? CurrentRobotMap { get; set; } = "26.7.2026_map_optimak";
        public string? CurrentRobotStation { get; set; } = "AP21";
        public int RobotConnectionStatus { get; set; } = 1;
        public bool RobotDispatchable { get; set; } = true;
        public bool RobotIsError { get; set; } = false;
        public List<string> RobotAlarms { get; set; } = [];
        public string? RobotCurrentOrderId { get; set; } = null;
        public bool KeepOldMapOnSwitch { get; set; } = false;
        public bool AlarmAfterSwitch { get; set; } = false;

        public RdsCoreRobotLockInfo? RobotLockInfo { get; set; }

        public RdsCoreKnownOrderState DetailsState { get; set; } = RdsCoreKnownOrderState.Waiting;
        public RdsCoreKnownOrderState TransitionOrderState { get; set; } = RdsCoreKnownOrderState.Unknown;

        public Task<RdsCoreSetOrderResult> SetOrderAsync(RdsCoreNormalOrder order, Guid correlationId, CancellationToken cancellationToken = default)
        {
            SetOrderCount++;
            Orders.Add(order);
            if (SetOrderError is not null) throw SetOrderError;
            return Task.FromResult(new RdsCoreSetOrderResult(
                true, true, true, 200, 0, "success", false, RdsCoreOrderDiagnostics.Empty, JsonDocument.Parse("{}").RootElement));
        }

        public Task<RdsCoreOrderDetails> GetOrderDetailsAsync(string orderId, Guid correlationId, CancellationToken cancellationToken = default)
        {
            var isTransition = orderId.Contains(":map-prep:");
            var state = isTransition ? TransitionOrderState : DetailsState;
            return Task.FromResult(new RdsCoreOrderDetails(
                orderId,
                null,
                new RdsCoreOrderState(state, state.ToString()),
                [],
                RdsCoreOrderDiagnostics.Empty,
                JsonDocument.Parse("{}").RootElement));
        }

        public Task<IReadOnlyList<RdsCoreRobotTelemetry>> GetRobotsStatusAsync(Guid correlationId, CancellationToken cancellationToken = default)
        {
            if (RobotsStatusError is not null) throw RobotsStatusError;
            var telemetry = new RdsCoreRobotTelemetry(
                "Sfl-Cbd15",
                "Sfl-Cbd15",
                CurrentRobotMap,
                CurrentRobotStation,
                null,
                RobotConnectionStatus,
                RobotDispatchable,
                false,
                RobotIsError,
                95.0,
                0, 0, 0, null,
                RobotCurrentOrderId,
                null,
                RobotAlarms,
                RobotLockInfo);
            return Task.FromResult<IReadOnlyList<RdsCoreRobotTelemetry>>([telemetry]);
        }

        public Task<RdsCoreSimpleResult> LockAsync(IReadOnlyList<string> vehicles, Guid correlationId, CancellationToken cancellationToken = default)
        {
            LockCount++;
            if (LockError is not null) throw LockError;
            RobotDispatchable = false;
            RobotLockInfo ??= new RdsCoreRobotLockInfo(true, "10.0.0.1", "TKG", 5000, "1634023715", 2, "ok");
            return Task.FromResult(new RdsCoreSimpleResult(true, true, 200, 0, "ok", JsonDocument.Parse("{}").RootElement));
        }

        public Task<RdsCoreSimpleResult> UnlockAsync(IReadOnlyList<string> vehicles, Guid correlationId, CancellationToken cancellationToken = default)
        {
            UnlockCount++;
            if (UnlockError is not null) throw UnlockError;
            RobotDispatchable = true;
            RobotLockInfo = new RdsCoreRobotLockInfo(false, null, null, null, null, null, null);
            return Task.FromResult(new RdsCoreSimpleResult(true, true, 200, 0, "ok", JsonDocument.Parse("{}").RootElement));
        }

        public Task<RdsCoreSimpleResult> SwitchMapAsync(string vehicle, string mapName, Guid correlationId, CancellationToken cancellationToken = default)
        {
            SwitchMapCount++;
            LastSwitchedMap = mapName;
            if (SwitchMapError is not null) throw SwitchMapError;
            if (!KeepOldMapOnSwitch)
            {
                CurrentRobotMap = mapName;
            }
            if (AlarmAfterSwitch)
            {
                RobotIsError = true;
                RobotAlarms.Add("LOCALIZATION_LOST");
            }
            return Task.FromResult(new RdsCoreSimpleResult(true, true, 200, 0, "ok", JsonDocument.Parse("{}").RootElement));
        }

        public Task<RdsCoreTerminateResult> TerminateAsync(string orderId, Guid correlationId, CancellationToken cancellationToken = default)
        {
            TerminateCount++;
            return Task.FromResult(new RdsCoreTerminateResult(true, true, 200, 0, "success", JsonDocument.Parse("{}").RootElement));
        }

        public Task<RdsCoreOrderList> GetOrdersAsync(RdsCoreOrderQuery query, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RdsCoreOrderDetails>> GetOrderDetailsByExternalIdAsync(string externalId, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreOrderDetails> GetOrderDetailsByBlockIdAsync(string blockId, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreBlockDetails> GetBlockDetailsByIdAsync(string blockId, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreSimpleResult> SetPriorityAsync(string orderId, int priority, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreSimpleResult> ClearRobotAllErrorAsync(IReadOnlyList<string> vehicles, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreSimpleResult> GotoSitePauseAsync(IReadOnlyList<string> vehicles, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreSimpleResult> GotoSiteResumeAsync(IReadOnlyList<string> vehicles, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreSimpleResult> ManualFinishedAsync(IReadOnlyList<string> vehicles, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreSimpleResult> RedoFailedOrderAsync(IReadOnlyList<string> vehicles, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RdsCoreSimpleResult> SetDispatchableAsync(IReadOnlyList<string> vehicles, string type, Guid correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
