namespace TkgPda.Domain;

public static class TransportStateMachine
{
    private static readonly IReadOnlyDictionary<TransportRequestStatus, IReadOnlySet<TransportRequestStatus>> LegalTransitions =
        new Dictionary<TransportRequestStatus, IReadOnlySet<TransportRequestStatus>>
        {
            [TransportRequestStatus.Created] = Set(TransportRequestStatus.Validating, TransportRequestStatus.Cancelled),
            [TransportRequestStatus.Validating] = Set(TransportRequestStatus.Queued, TransportRequestStatus.Failed, TransportRequestStatus.Cancelled),
            [TransportRequestStatus.Queued] = Set(TransportRequestStatus.SentToRds, TransportRequestStatus.Cancelled),
            [TransportRequestStatus.SentToRds] = Set(TransportRequestStatus.Running, TransportRequestStatus.Failed),
            [TransportRequestStatus.Running] = Set(TransportRequestStatus.Completed, TransportRequestStatus.Failed),
            [TransportRequestStatus.Completed] = Set(),
            [TransportRequestStatus.Failed] = Set(),
            [TransportRequestStatus.Cancelled] = Set()
        };

    public static IReadOnlySet<TransportRequestStatus> NextStates(TransportRequestStatus current) => LegalTransitions[current];

    public static bool CanTransition(TransportRequestStatus current, TransportRequestStatus next) =>
        LegalTransitions[current].Contains(next);

    public static void EnsureTransition(TransportRequestStatus current, TransportRequestStatus next)
    {
        if (!CanTransition(current, next))
        {
            throw new WorkflowRuleException(
                "ILLEGAL_STATUS_TRANSITION",
                $"Transport request cannot transition from {current.ToDatabase()} to {next.ToDatabase()}.");
        }
    }

    public static bool IsReservationActive(TransportRequestStatus status) => status is
        TransportRequestStatus.Created or
        TransportRequestStatus.Validating or
        TransportRequestStatus.Queued or
        TransportRequestStatus.SentToRds or
        TransportRequestStatus.Running;

    public static bool CanCancelBeforeExecution(TransportRequestStatus status) => status is
        TransportRequestStatus.Created or
        TransportRequestStatus.Validating or
        TransportRequestStatus.Queued;

    private static HashSet<TransportRequestStatus> Set(params TransportRequestStatus[] states) => [.. states];
}
