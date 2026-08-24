namespace TkgPda.Domain;

public sealed class WorkflowRuleException : InvalidOperationException
{
    public WorkflowRuleException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public static class WorkflowErrorCodes
{
    public const string CancellationAfterExecutionDeferred = "CANCELLATION_AFTER_EXECUTION_DEFERRED";
    public const string RdsCancellationPending = "RDS_CANCELLATION_PENDING";
}

public sealed record SlotState(
    OccupancyState Occupancy,
    LoadState Load,
    Guid? CurrentPartId,
    Guid? CurrentContainerTypeId);

public sealed record SlotSnapshot(
    Guid Id,
    Guid MachineId,
    Guid LayoutId,
    SlotRole Role,
    bool IsEnabled,
    Guid? DefaultContainerTypeId,
    IReadOnlySet<Guid> KnownPartIds,
    SlotState State);

public sealed record CreateTransportRequest(
    string IdempotencyKey,
    RequestOrigin Origin,
    Guid? RequestedBy,
    Guid MachineId,
    Guid VariantId,
    Guid LayoutId,
    Guid? NavigationProfileId,
    Guid? ProductGroupId,
    Guid? PartId,
    Guid? ContainerTypeId,
    TransportTaskType TaskType,
    Guid? SourceSlotId,
    Guid? DestinationSlotId,
    int Priority = 100)
{
    public Guid ReservedSlotId => TaskType == TransportTaskType.BringEmpty
        ? DestinationSlotId ?? throw new WorkflowRuleException("DESTINATION_REQUIRED", "BRING_EMPTY requires a machine destination slot.")
        : SourceSlotId ?? throw new WorkflowRuleException("SOURCE_REQUIRED", $"{TaskType.ToDatabase()} requires a machine source slot.");

    public void ValidateShape()
    {
        if (string.IsNullOrWhiteSpace(IdempotencyKey))
        {
            throw new WorkflowRuleException("IDEMPOTENCY_KEY_REQUIRED", "An idempotency key is required.");
        }

        if (IdempotencyKey.Length > 200)
        {
            throw new WorkflowRuleException("IDEMPOTENCY_KEY_TOO_LONG", "The idempotency key cannot exceed 200 characters.");
        }

        if (Priority < 0)
        {
            throw new WorkflowRuleException("INVALID_PRIORITY", "Priority cannot be negative.");
        }

        _ = ReservedSlotId;

        if (SourceSlotId.HasValue && SourceSlotId == DestinationSlotId)
        {
            throw new WorkflowRuleException("SAME_SOURCE_DESTINATION", "Source and destination slots must be different.");
        }
    }
}

public sealed record TransportRequestSnapshot(
    Guid Id,
    long RequestNo,
    string IdempotencyKey,
    TransportRequestStatus Status,
    TransportTaskType TaskType,
    RequestOrigin Origin,
    Guid? RequestedBy,
    Guid MachineId,
    Guid VariantId,
    Guid LayoutId,
    Guid? NavigationProfileId,
    Guid? ProductGroupId,
    Guid? PartId,
    Guid? ContainerTypeId,
    Guid? SourceSlotId,
    Guid? DestinationSlotId,
    int Priority);

public sealed record CreateTransportResult(TransportRequestSnapshot Request, bool WasIdempotentReplay);

public sealed record CancelTransportResult(TransportRequestSnapshot Request, bool WasAlreadyCancelled);

public sealed record LoadStateUpdateRequest(
    Guid MachineId,
    Guid SlotId,
    LoadState TargetLoadState,
    Guid ActorUserId);

public sealed record LoadStateUpdateResult(
    Guid MachineId,
    Guid SlotId,
    OccupancyState OccupancyState,
    LoadState LoadState,
    long RowVersion,
    bool WasIdempotentReplay);

public sealed record ExecutionOutcome(
    TransportRequestStatus Status,
    string? FailureCode = null,
    string? FailureMessage = null,
    string? EvidenceType = null);

public sealed record VariantSelection(
    Guid MachineId,
    Guid VariantId,
    Guid LayoutId,
    Guid? NavigationProfileId,
    Guid? SelectedBy);
