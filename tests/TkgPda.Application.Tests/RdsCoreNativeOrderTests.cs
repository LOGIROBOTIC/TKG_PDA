using TkgPda.Application;
using TkgPda.Domain;
using System.Text.Json;
using Xunit;

namespace TkgPda.Application.Tests;

public sealed class RdsCoreNativeOrderTests
{
    [Fact]
    public void Normal_order_uses_request_identity_deterministic_blocks_and_is_always_sealed()
    {
        var requestId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var order = RdsCoreNormalOrder.Create(
            requestId,
            new RdsCoreNavigationBlock(RdsCoreNormalOrder.PickupBlockId(requestId), "SIM-A"),
            new RdsCoreNavigationBlock(RdsCoreNormalOrder.DropoffBlockId(requestId), "SIM-B"));

        Assert.Equal(requestId.ToString("D"), order.Id);
        Assert.Equal($"{requestId:D}:pickup", order.Blocks[0].BlockId);
        Assert.Equal($"{requestId:D}:dropoff", order.Blocks[1].BlockId);
        Assert.True(order.Complete);
    }

    [Fact]
    public void Normal_order_rejects_non_deterministic_ids_and_missing_locations()
    {
        var requestId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => RdsCoreNormalOrder.Create(
            requestId,
            new RdsCoreNavigationBlock("different:pickup", "A"),
            new RdsCoreNavigationBlock(RdsCoreNormalOrder.DropoffBlockId(requestId), "B")));
        Assert.Throws<ArgumentException>(() => RdsCoreNormalOrder.Create(
            requestId,
            new RdsCoreNavigationBlock(RdsCoreNormalOrder.PickupBlockId(requestId), " "),
            new RdsCoreNavigationBlock(RdsCoreNormalOrder.DropoffBlockId(requestId), "B")));
    }

    [Fact]
    public void Typed_JSON_argument_fields_reject_non_object_values()
    {
        var requestId = Guid.NewGuid();
        var scalar = JsonDocument.Parse("1").RootElement.Clone();

        Assert.Throws<ArgumentException>(() => RdsCoreNormalOrder.Create(
            requestId,
            new RdsCoreOperationBlock(RdsCoreNormalOrder.PickupBlockId(requestId), "A", "Explicit", scalar),
            new RdsCoreNavigationBlock(RdsCoreNormalOrder.DropoffBlockId(requestId), "B")));
    }

    [Theory]
    [InlineData(RdsCoreKnownOrderState.Created, RdsCoreReconciliationMeaning.Pending)]
    [InlineData(RdsCoreKnownOrderState.ToBeDispatched, RdsCoreReconciliationMeaning.Pending)]
    [InlineData(RdsCoreKnownOrderState.Waiting, RdsCoreReconciliationMeaning.Pending)]
    [InlineData(RdsCoreKnownOrderState.Running, RdsCoreReconciliationMeaning.Running)]
    [InlineData(RdsCoreKnownOrderState.Suspended, RdsCoreReconciliationMeaning.SuspendedRecoverable)]
    [InlineData(RdsCoreKnownOrderState.Finished, RdsCoreReconciliationMeaning.CompletedCandidate)]
    [InlineData(RdsCoreKnownOrderState.Failed, RdsCoreReconciliationMeaning.FailedCandidate)]
    [InlineData(RdsCoreKnownOrderState.Error, RdsCoreReconciliationMeaning.FailedCandidate)]
    [InlineData(RdsCoreKnownOrderState.Stopped, RdsCoreReconciliationMeaning.CancelledCandidate)]
    [InlineData(RdsCoreKnownOrderState.Unknown, RdsCoreReconciliationMeaning.Unknown)]
    public void Reconciliation_classifies_every_RDS_order_state_without_mutating_domain(
        RdsCoreKnownOrderState state,
        RdsCoreReconciliationMeaning expected)
    {
        var decision = RdsCoreOrderReconciliationPolicy.Decide(
            TransportRequestStatus.SentToRds,
            new(state, state.ToString()));

        Assert.Equal(expected, decision.Meaning);
        for (var index = 0; index < decision.LegalTransitionPath.Count; index++)
        {
            var previous = index == 0 ? TransportRequestStatus.SentToRds : decision.LegalTransitionPath[index - 1];
            Assert.True(TransportStateMachine.CanTransition(previous, decision.LegalTransitionPath[index]));
        }
    }

    [Fact]
    public void Finished_from_queued_uses_only_legal_existing_transitions()
    {
        var decision = RdsCoreOrderReconciliationPolicy.Decide(
            TransportRequestStatus.Queued,
            new(RdsCoreKnownOrderState.Finished, "FINISHED"));

        Assert.Equal(
            [TransportRequestStatus.SentToRds, TransportRequestStatus.Running, TransportRequestStatus.Completed],
            decision.LegalTransitionPath);
        Assert.True(decision.AutomaticMutationAllowed);
    }

    [Fact]
    public void Suspended_unknown_and_terminal_conflicts_never_generate_mutations()
    {
        var suspended = RdsCoreOrderReconciliationPolicy.Decide(
            TransportRequestStatus.Running,
            new(RdsCoreKnownOrderState.Suspended, "SUSPENDED"));
        var unknown = RdsCoreOrderReconciliationPolicy.Decide(
            TransportRequestStatus.Running,
            new(RdsCoreKnownOrderState.Unknown, "FUTURE"));
        var late = RdsCoreOrderReconciliationPolicy.Decide(
            TransportRequestStatus.Completed,
            new(RdsCoreKnownOrderState.Failed, "FAILED"));

        Assert.All([suspended, unknown, late], value =>
        {
            Assert.False(value.AutomaticMutationAllowed);
            Assert.Empty(value.LegalTransitionPath);
            Assert.NotNull(value.UnresolvedReason);
        });
    }
}
