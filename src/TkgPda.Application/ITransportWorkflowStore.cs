using TkgPda.Domain;

namespace TkgPda.Application;

public interface ITransportWorkflowStore
{
    Task<CreateTransportResult> CreateAndQueueAsync(CreateTransportRequest request, CancellationToken cancellationToken = default);

    Task<CancelTransportResult> CancelAsync(Guid requestId, Guid? actorUserId, CancellationToken cancellationToken = default);

    Task<TransportRequestSnapshot> RecordExecutionTransitionAsync(
        Guid requestId,
        TransportRequestStatus nextStatus,
        CancellationToken cancellationToken = default);

    Task<TransportRequestSnapshot> RecordExecutionOutcomeAsync(
        Guid requestId,
        ExecutionOutcome outcome,
        CancellationToken cancellationToken = default);

    Task<LoadStateUpdateResult> UpdateLoadStateAsync(
        LoadStateUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task SwitchVariantAsync(VariantSelection selection, CancellationToken cancellationToken = default);
}
