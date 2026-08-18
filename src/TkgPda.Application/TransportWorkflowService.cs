using TkgPda.Domain;

namespace TkgPda.Application;

public sealed class TransportWorkflowService(ITransportWorkflowStore store)
{
    public Task<CreateTransportResult> CreateAsync(
        CreateTransportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.ValidateShape();
        return store.CreateAndQueueAsync(request, cancellationToken);
    }

    public Task<CancelTransportResult> CancelAsync(
        Guid requestId,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (requestId == Guid.Empty)
        {
            throw new WorkflowRuleException("REQUEST_ID_REQUIRED", "A transport request ID is required.");
        }

        return store.CancelAsync(requestId, actorUserId, cancellationToken);
    }

    public Task<TransportRequestSnapshot> RecordExecutionTransitionAsync(
        Guid requestId,
        TransportRequestStatus nextStatus,
        CancellationToken cancellationToken = default)
    {
        if (requestId == Guid.Empty)
        {
            throw new WorkflowRuleException("REQUEST_ID_REQUIRED", "A transport request ID is required.");
        }

        if (nextStatus is not (TransportRequestStatus.SentToRds or TransportRequestStatus.Running or TransportRequestStatus.Completed))
        {
            throw new WorkflowRuleException("UNSUPPORTED_EXECUTION_UPDATE", "Phase 03 only records dispatch, running and successful completion transitions.");
        }

        return store.RecordExecutionTransitionAsync(requestId, nextStatus, cancellationToken);
    }

    public Task SwitchVariantAsync(VariantSelection selection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return store.SwitchVariantAsync(selection, cancellationToken);
    }
}
