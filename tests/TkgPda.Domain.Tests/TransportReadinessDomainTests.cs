using TkgPda.Domain;
using Xunit;

namespace TkgPda.Domain.Tests;

public sealed class TransportReadinessDomainTests
{
    [Fact]
    public void Omitted_requires_preparation_defaults_to_not_required()
    {
        var command = Command(requiresPreparation: null);

        Assert.Equal(TransportReadinessState.NotRequired, command.InitialReadinessState);
    }

    [Fact]
    public void Requires_preparation_false_yields_not_required()
    {
        var command = Command(requiresPreparation: false);

        Assert.Equal(TransportReadinessState.NotRequired, command.InitialReadinessState);
    }

    [Fact]
    public void Requires_preparation_true_yields_waiting_preparation()
    {
        var command = Command(requiresPreparation: true);

        Assert.Equal(TransportReadinessState.WaitingPreparation, command.InitialReadinessState);
    }

    [Fact]
    public void Mark_ready_is_allowed_from_waiting_preparation_on_a_non_terminal_request()
    {
        var exception = Record.Exception(() =>
            TransportReadinessRules.EnsureCanMarkReady(TransportRequestStatus.Queued, TransportReadinessState.WaitingPreparation));

        Assert.Null(exception);
    }

    [Fact]
    public void Mark_ready_is_rejected_when_readiness_is_not_required()
    {
        var exception = Assert.Throws<WorkflowRuleException>(() =>
            TransportReadinessRules.EnsureCanMarkReady(TransportRequestStatus.Queued, TransportReadinessState.NotRequired));

        Assert.Equal("READINESS_NOT_APPLICABLE", exception.Code);
    }

    [Theory]
    [InlineData(TransportRequestStatus.Completed)]
    [InlineData(TransportRequestStatus.Failed)]
    [InlineData(TransportRequestStatus.Cancelled)]
    public void Mark_ready_is_rejected_on_a_terminal_request(TransportRequestStatus terminalStatus)
    {
        var exception = Assert.Throws<WorkflowRuleException>(() =>
            TransportReadinessRules.EnsureCanMarkReady(terminalStatus, TransportReadinessState.WaitingPreparation));

        Assert.Equal("REQUEST_TERMINAL", exception.Code);
    }

    [Fact]
    public void Repeated_mark_ready_on_an_already_ready_request_is_a_no_op()
    {
        Assert.True(TransportReadinessRules.IsMarkReadyNoOp(TransportReadinessState.Ready));
    }

    [Fact]
    public void Ready_cannot_be_reached_again_through_ensure_can_mark_ready_once_already_ready()
    {
        // WP-05A: READY never regresses to WAITING_PREPARATION. The only sanctioned
        // path back into EnsureCanMarkReady for an already-Ready request is the
        // idempotent no-op guard (IsMarkReadyNoOp) checked by callers *before*
        // this rule runs; the rule itself has no transition that produces
        // WaitingPreparation from Ready, so calling it directly on a Ready
        // request must still reject rather than silently regress.
        var exception = Assert.Throws<WorkflowRuleException>(() =>
            TransportReadinessRules.EnsureCanMarkReady(TransportRequestStatus.Queued, TransportReadinessState.Ready));

        Assert.Equal("READINESS_NOT_APPLICABLE", exception.Code);
    }

    [Fact]
    public void Sql_readiness_states_are_complete_and_deterministic()
    {
        var codes = Enum.GetValues<TransportReadinessState>().Select(WorkflowCode.ToDatabase).ToArray();

        Assert.Equal(["NOT_REQUIRED", "WAITING_PREPARATION", "READY"], codes);

        foreach (var code in codes)
        {
            Assert.Equal(
                Enum.GetValues<TransportReadinessState>().Single(v => v.ToDatabase() == code),
                WorkflowCode.ParseReadinessState(code));
        }
    }

    private static CreateTransportRequest Command(bool? requiresPreparation) => requiresPreparation is null
        ? new CreateTransportRequest(
            "readiness-domain-key",
            RequestOrigin.LineOperator,
            null,
            MachineId,
            VariantId,
            LayoutId,
            null,
            null,
            null,
            null,
            TransportTaskType.BringEmpty,
            null,
            Guid.NewGuid())
        : new CreateTransportRequest(
            "readiness-domain-key",
            RequestOrigin.LineOperator,
            null,
            MachineId,
            VariantId,
            LayoutId,
            null,
            null,
            null,
            null,
            TransportTaskType.BringEmpty,
            null,
            Guid.NewGuid(),
            RequiresPreparation: requiresPreparation.Value);

    private static readonly Guid MachineId = Guid.NewGuid();
    private static readonly Guid VariantId = Guid.NewGuid();
    private static readonly Guid LayoutId = Guid.NewGuid();
}
