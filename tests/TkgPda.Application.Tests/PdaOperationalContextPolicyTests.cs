using TkgPda.Application;
using TkgPda.Domain;
using Xunit;

namespace TkgPda.Application.Tests;

public sealed class PdaOperationalContextPolicyTests
{
    [Fact]
    public void Machine_region_exposes_no_enabled_transport_action()
    {
        var actions = PdaOperationalContextPolicy.EvaluateActions(
            Slot(role: "MACHINE", occupancy: "VACANT", load: "UNKNOWN"),
            Actor(KnownRoleCodes.LineOperator),
            canOperate: true,
            hasActiveRequest: false);

        Assert.All(actions, action =>
        {
            Assert.False(action.Enabled);
            Assert.Equal("MACHINE_REGION_FORBIDDEN", action.DisabledCode);
        });
    }

    [Fact]
    public void Vacant_unknown_enables_bring_empty_for_authorized_line_operator()
    {
        var action = Action(Slot(occupancy: "VACANT", load: "UNKNOWN"), KnownRoleCodes.LineOperator, "BRING_EMPTY");

        Assert.True(action.Enabled);
        Assert.Null(action.DisabledCode);
    }

    [Fact]
    public void Occupied_empty_enables_remove_empty_for_authorized_operator()
    {
        var action = Action(Slot(occupancy: "OCCUPIED", load: "EMPTY"), KnownRoleCodes.Supervisor, "REMOVE_EMPTY");

        Assert.True(action.Enabled);
    }

    [Fact]
    public void Occupied_full_enables_remove_full_for_authorized_operator()
    {
        var action = Action(Slot(occupancy: "OCCUPIED", load: "FULL"), KnownRoleCodes.Admin, "REMOVE_FULL");

        Assert.True(action.Enabled);
    }

    [Fact]
    public void Warehouse_operator_receives_only_bring_empty_capability()
    {
        var actions = PdaOperationalContextPolicy.EvaluateActions(
            Slot(occupancy: "VACANT", load: "UNKNOWN"),
            Actor(KnownRoleCodes.WarehouseOperator),
            canOperate: true,
            hasActiveRequest: false);

        Assert.True(actions.Single(action => action.Action == "BRING_EMPTY").Enabled);
        Assert.All(actions.Where(action => action.Action != "BRING_EMPTY"), action =>
        {
            Assert.False(action.Enabled);
            Assert.Equal("OPERATION_NOT_ALLOWED_FOR_ROLE", action.DisabledCode);
        });
    }

    [Fact]
    public void Active_request_blocks_every_second_action_for_the_slot()
    {
        var actions = PdaOperationalContextPolicy.EvaluateActions(
            Slot(occupancy: "VACANT", load: "UNKNOWN"),
            Actor(KnownRoleCodes.LineOperator),
            canOperate: true,
            hasActiveRequest: true);

        Assert.All(actions, action =>
        {
            Assert.False(action.Enabled);
            Assert.Equal("SLOT_BUSY", action.DisabledCode);
        });
    }

    [Fact]
    public void Admin_web_context_never_receives_pda_transport_capabilities()
    {
        var actions = PdaOperationalContextPolicy.EvaluateActions(
            Slot(occupancy: "VACANT", load: "UNKNOWN"),
            Actor(KnownRoleCodes.Admin, deviceBound: false),
            canOperate: true,
            hasActiveRequest: false);

        Assert.All(actions, action => Assert.Equal("PDA_DEVICE_REQUIRED", action.DisabledCode));
    }

    [Theory]
    [InlineData("CREATED", null, null, false, "REQUESTED")]
    [InlineData("QUEUED", null, null, false, "QUEUED")]
    [InlineData("SENT_TO_RDS", null, null, false, "WAITING_FOR_ROBOT")]
    [InlineData("SENT_TO_RDS", 1000, "AGV-07", false, "ROBOT_ASSIGNED")]
    [InlineData("SENT_TO_RDS", 1000, "AGV-07", true, "RUNNING")]
    [InlineData("RUNNING", 1002, "AGV-07", true, "WAITING")]
    [InlineData("RUNNING", 1004, "AGV-07", true, "FAILED")]
    [InlineData("RUNNING", 1003, "AGV-07", true, "COMPLETED")]
    [InlineData("RUNNING", 1001, "AGV-07", true, "TERMINATED")]
    public void Execution_projection_maps_tkg_and_persisted_rds_state_without_claiming_motion(
        string tkgStatus,
        int? rdsStatus,
        string? agvId,
        bool started,
        string expected)
    {
        var request = Request(
            WorkflowCode.ParseTransportStatus(tkgStatus),
            rdsStatus is null
                ? null
                : new RdsTaskDataRecord(
                    "RDS-1", "LABEL", "OUT-1", agvId, rdsStatus, null,
                    DateTimeOffset.UtcNow, started ? DateTimeOffset.UtcNow : null, null, DateTimeOffset.UtcNow));

        var projection = PdaExecutionProjector.Project(request, technicalAuthorized: false);

        Assert.Equal(expected, projection.State);
    }

    [Theory]
    [InlineData("COMPLETED", 1004, "COMPLETED")]
    [InlineData("FAILED", 1003, "FAILED")]
    [InlineData("CANCELLED", 1003, "TERMINATED")]
    public void Terminal_tkg_business_state_takes_precedence_over_stale_rds_state(
        string tkgStatus,
        int rdsStatus,
        string expected)
    {
        var request = Request(
            WorkflowCode.ParseTransportStatus(tkgStatus),
            new RdsTaskDataRecord(
                "RDS-1", "LABEL", "OUT-1", "AGV-07", rdsStatus, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow));

        var projection = PdaExecutionProjector.Project(request, technicalAuthorized: false);

        Assert.Equal(expected, projection.State);
    }

    [Fact]
    public void Known_failure_is_mapped_to_operator_safe_contract_and_technical_text_is_role_sensitive()
    {
        var request = Request(TransportRequestStatus.Running, failureCode: "60008", failureMessage: "fail_lock_bin raw detail");

        var operatorView = PdaExecutionProjector.Project(request, technicalAuthorized: false);
        var supervisorView = PdaExecutionProjector.Project(request, technicalAuthorized: true);

        Assert.Equal("WORKSITE_BUSY", operatorView.FailureCode);
        Assert.Equal("WORKSITE", operatorView.FailureCategory);
        Assert.NotNull(operatorView.OperatorMessage);
        Assert.Null(operatorView.TechnicalMessage);
        Assert.Equal("fail_lock_bin raw detail", supervisorView.TechnicalMessage);
    }

    [Fact]
    public void Core_traffic_warning_projects_waiting_not_failed()
    {
        var projection = PdaExecutionProjector.Project(
            Request(TransportRequestStatus.Running, failureCode: "54002"),
            technicalAuthorized: false);

        Assert.Equal("WAITING", projection.State);
        Assert.Equal("TRAFFIC_WAITING", projection.FailureCode);
    }

    [Fact]
    public void Missing_rds_mapping_is_safe_and_explicit_without_invented_site_id()
    {
        var context = PdaOperationalContextPolicy.Project(
            Data(Slot(occupancy: "VACANT", load: "UNKNOWN")),
            Actor(KnownRoleCodes.LineOperator));
        var slot = Assert.Single(context.Slots);

        // No raw WorkSite record reaches the PDA contract; an unmapped slot is
        // reported honestly through readiness alone, with no invented site id.
        Assert.False(slot.ExecutionReadiness.RdsWorkSiteMapped);
        Assert.False(slot.ExecutionReadiness.IsExecutionReady);
        Assert.Equal("RDS_WORKSITE_NOT_MAPPED", slot.ExecutionReadiness.ReadinessCode);
    }

    [Fact]
    public void Persisted_rds_mapping_is_projected_exactly_without_invention()
    {
        var workSite = new RdsWorkSiteProjectionRecord(
            true, "SITE-41", "A1", "GROUP-X", 1, 1, 0, 0, 0, "known-content", "AGV-9", DateTimeOffset.UtcNow);
        var context = PdaOperationalContextPolicy.Project(
            Data(Slot(occupancy: "OCCUPIED", load: "FULL", workSite: workSite)),
            Actor(KnownRoleCodes.Supervisor));
        var slot = Assert.Single(context.Slots);

        // The persisted mapping drives readiness exactly, but the raw WorkSite
        // record itself is not serialized to the PDA — not even for a Supervisor.
        Assert.True(slot.ExecutionReadiness.RdsWorkSiteMapped);
        Assert.True(slot.ExecutionReadiness.IsExecutionReady);
        Assert.Null(slot.ExecutionReadiness.ReadinessCode);
    }

    [Theory]
    [InlineData(0, 1, 0, "RDS_WORKSITE_DISABLED")]
    [InlineData(0, 0, 1, "RDS_WORKSITE_SYNC_FAILED")]
    [InlineData(1, 0, 0, "RDS_WORKSITE_LOCKED")]
    public void Mapped_unavailable_rds_evidence_is_not_execution_ready(
        int locked,
        int disabled,
        int syncFailed,
        string expectedCode)
    {
        var workSite = new RdsWorkSiteProjectionRecord(
            true, "SITE-BUSY", null, null, 1, 0, locked, disabled, syncFailed, string.Empty, null, DateTimeOffset.UtcNow);
        var context = PdaOperationalContextPolicy.Project(
            Data(Slot(occupancy: "VACANT", load: "UNKNOWN", workSite: workSite)),
            Actor(KnownRoleCodes.LineOperator));
        var readiness = Assert.Single(context.Slots).ExecutionReadiness;

        Assert.True(readiness.RdsWorkSiteMapped);
        Assert.False(readiness.IsExecutionReady);
        Assert.Equal(expectedCode, readiness.ReadinessCode);
    }

    private static PdaAvailableActionRecord Action(SlotContextDataRecord slot, string role, string operation) =>
        PdaOperationalContextPolicy.EvaluateActions(slot, Actor(role), true, false)
            .Single(action => action.Action == operation);

    private static PdaActorContext Actor(string role, bool deviceBound = true) =>
        new(Guid.NewGuid(), new HashSet<string>(StringComparer.Ordinal) { role }, deviceBound);

    private static SlotContextDataRecord Slot(
        string role = "STAGING",
        string occupancy = "UNKNOWN",
        string load = "UNKNOWN",
        RdsWorkSiteProjectionRecord? workSite = null) =>
        new(
            Guid.NewGuid(), "SLOT-1", "Test Slot", role, true, role != "MACHINE",
            null, null, 0.1m, 0.1m, 0.2m, 0.2m, 1,
            occupancy, load, null, null, null, null, 0, null,
            workSite ?? new RdsWorkSiteProjectionRecord(false, null, null, null, null, null, null, null, null, null, null, null));

    private static OperationalContextDataRecord Data(SlotContextDataRecord slot) =>
        new(
            new MachineSummaryRecord(
                Guid.NewGuid(), "M-1", "Machine 1", 1,
                new MachinePermissionRecord(Guid.NewGuid(), true, true, false, false)),
            Guid.NewGuid(), "V1", "Variant 1",
            Guid.NewGuid(), "L1", "Layout 1",
            null, null, 0, null,
            [], [slot], [], [], []);

    private static ActiveTransportDataRecord Request(
        TransportRequestStatus status,
        RdsTaskDataRecord? rdsTask = null,
        string? failureCode = null,
        string? failureMessage = null) =>
        new(
            Guid.NewGuid(), 42, TransportTaskType.BringEmpty, status,
            Guid.NewGuid(), "operator", true, null, Guid.NewGuid(),
            null, null, null, 100, DateTimeOffset.UtcNow,
            failureCode, failureMessage, Guid.NewGuid(), rdsTask);
}
