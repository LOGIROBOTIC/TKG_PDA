using TkgPda.Domain;

namespace TkgPda.Application;

public interface ITransportWorkflowStore
{
    Task<CreateTransportResult> CreateAndQueueAsync(CreateTransportRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// WP-04: creates a TransportBatch and its child TransportRequests
    /// atomically (all-or-nothing) within a single transaction, reusing the
    /// same per-item validation/business rules as
    /// <see cref="CreateAndQueueAsync"/>. Replaying the same batch
    /// idempotency key returns the existing batch instead of creating
    /// duplicate children.
    /// </summary>
    Task<TransportBatchResult> CreateBatchAndQueueAsync(CreateTransportBatchRequest request, CancellationToken cancellationToken = default);

    Task<CancelTransportResult> CancelAsync(Guid requestId, Guid? actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// WP-05A: transitions a request's BUSINESS readiness from
    /// WAITING_PREPARATION to READY and records ready_at. Idempotent when
    /// already READY. Never touches the execution state machine.
    /// </summary>
    Task<TransportRequestSnapshot> MarkReadyAsync(Guid requestId, Guid? actorUserId, CancellationToken cancellationToken = default);

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
