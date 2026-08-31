namespace TkgPda.Domain;

public static class TransportRules
{
    public static void ValidateSuppliedSlot(CreateTransportRequest request, SlotSnapshot slot)
    {
        if (!slot.IsEnabled)
        {
            throw new WorkflowRuleException("SLOT_DISABLED", $"Slot {slot.Id} is disabled.");
        }

        if (slot.Role == SlotRole.Machine)
        {
            throw new WorkflowRuleException("MACHINE_REGION_FORBIDDEN", "Machine/cell display regions cannot start or receive a transport.");
        }

        if (slot.MachineId != request.MachineId || slot.LayoutId != request.LayoutId)
        {
            throw new WorkflowRuleException("CROSS_MACHINE_SLOT", "Every supplied slot must belong to the request's active machine layout.");
        }
    }

    public static void ValidateReservation(CreateTransportRequest request, SlotSnapshot reservedSlot)
    {
        ValidateSuppliedSlot(request, reservedSlot);

        if (reservedSlot.Role == SlotRole.EmptyStorage)
        {
            throw new WorkflowRuleException(
                "EMPTY_STORAGE_MACHINE_SLOT_FORBIDDEN",
                "EMPTY_STORAGE is reserved for backend buffer allocation and cannot be used as the machine-side transport slot.");
        }

        var expected = request.TaskType switch
        {
            TransportTaskType.BringEmpty => new SlotState(OccupancyState.Vacant, LoadState.Unknown, null, null),
            TransportTaskType.RemoveFull => reservedSlot.State with { Occupancy = OccupancyState.Occupied, Load = LoadState.Full },
            TransportTaskType.RemoveEmpty => reservedSlot.State with { Occupancy = OccupancyState.Occupied, Load = LoadState.Empty },
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.TaskType, null)
        };

        if (reservedSlot.State.Occupancy != expected.Occupancy || reservedSlot.State.Load != expected.Load)
        {
            throw new WorkflowRuleException(
                "SLOT_STATE_MISMATCH",
                $"{request.TaskType.ToDatabase()} cannot reserve slot state {reservedSlot.State.Occupancy}/{reservedSlot.State.Load}.");
        }

        ValidateAssociations(request, reservedSlot);
    }

    public static SlotState CancellationState(TransportTaskType taskType, SlotState current) => taskType switch
    {
        TransportTaskType.BringEmpty => new SlotState(OccupancyState.Vacant, LoadState.Unknown, null, null),
        TransportTaskType.RemoveFull => current with { Occupancy = OccupancyState.Occupied, Load = LoadState.Full },
        TransportTaskType.RemoveEmpty => current with { Occupancy = OccupancyState.Occupied, Load = LoadState.Empty },
        _ => throw new ArgumentOutOfRangeException(nameof(taskType), taskType, null)
    };

    public static SlotState CompletionState(TransportTaskType taskType, Guid? partId, Guid? containerTypeId) => taskType switch
    {
        TransportTaskType.BringEmpty => new SlotState(OccupancyState.Occupied, LoadState.Empty, partId, containerTypeId),
        TransportTaskType.RemoveFull or TransportTaskType.RemoveEmpty => new SlotState(OccupancyState.Vacant, LoadState.Unknown, null, null),
        _ => throw new ArgumentOutOfRangeException(nameof(taskType), taskType, null)
    };

    /// <summary>
    /// Customer Workflow — Empty Container Buffer: BRING_EMPTY draws an empty
    /// container from a configured EMPTY_STORAGE slot (its source);
    /// REMOVE_EMPTY returns one to a configured EMPTY_STORAGE slot (its
    /// destination). REMOVE_FULL never touches the buffer. Returns null for
    /// task types that have no buffer side.
    /// </summary>
    public static bool RequiresBufferSlot(TransportTaskType taskType) =>
        taskType is TransportTaskType.BringEmpty or TransportTaskType.RemoveEmpty;

    public static Guid? BufferSlotId(TransportTaskType taskType, Guid? sourceSlotId, Guid? destinationSlotId) =>
        taskType switch
        {
            TransportTaskType.BringEmpty => sourceSlotId,
            TransportTaskType.RemoveEmpty => destinationSlotId,
            _ => null
        };

    /// <summary>
    /// The buffer slot's required state at request-creation time: a full
    /// container waiting to be brought in (BRING_EMPTY's source), or empty
    /// space waiting to receive one (REMOVE_EMPTY's destination).
    /// </summary>
    public static SlotState ExpectedBufferState(TransportTaskType taskType) => taskType switch
    {
        TransportTaskType.BringEmpty => new SlotState(OccupancyState.Occupied, LoadState.Empty, null, null),
        TransportTaskType.RemoveEmpty => new SlotState(OccupancyState.Vacant, LoadState.Unknown, null, null),
        _ => throw new ArgumentOutOfRangeException(nameof(taskType), taskType, "This task type has no buffer slot.")
    };

    /// <summary>
    /// The buffer slot's state once the transport completes: emptied out
    /// (BRING_EMPTY took its container), or now holding the container that
    /// was just removed (REMOVE_EMPTY's destination).
    /// </summary>
    public static SlotState BufferCompletionState(TransportTaskType taskType, Guid? partId, Guid? containerTypeId) =>
        taskType switch
        {
            TransportTaskType.BringEmpty => new SlotState(OccupancyState.Vacant, LoadState.Unknown, null, null),
            TransportTaskType.RemoveEmpty => new SlotState(OccupancyState.Occupied, LoadState.Empty, partId, containerTypeId),
            _ => throw new ArgumentOutOfRangeException(nameof(taskType), taskType, "This task type has no buffer slot.")
        };

    /// <summary>
    /// Customer UX — Automatic Machine Slot Resolution: the machine-side
    /// occupancy/load state an eligible, part-mapped slot must currently be
    /// in for this operation. BRING_EMPTY needs an empty destination to fill;
    /// REMOVE_FULL/REMOVE_EMPTY need a source already holding what they take
    /// away. Mirrors ValidateReservation's own per-operation expectation, but
    /// as a plain lookup the SQL-side resolver can use directly.
    /// </summary>
    public static (OccupancyState Occupancy, LoadState Load) ExpectedMachineSlotState(TransportTaskType taskType) => taskType switch
    {
        TransportTaskType.BringEmpty => (OccupancyState.Vacant, LoadState.Unknown),
        TransportTaskType.RemoveFull => (OccupancyState.Occupied, LoadState.Full),
        TransportTaskType.RemoveEmpty => (OccupancyState.Occupied, LoadState.Empty),
        _ => throw new ArgumentOutOfRangeException(nameof(taskType), taskType, null)
    };

    /// <summary>
    /// A buffer slot must be configured master data (slot_role =
    /// EMPTY_STORAGE) — never inferred from name, WorkSite, machine code or
    /// coordinates — and must currently be in the state this operation
    /// expects of it.
    /// </summary>
    public static void ValidateBufferSlot(CreateTransportRequest request, SlotSnapshot bufferSlot)
    {
        if (bufferSlot.Role != SlotRole.EmptyStorage)
        {
            throw new WorkflowRuleException(
                "EMPTY_BUFFER_SLOT_NOT_CONFIGURED",
                $"Slot {bufferSlot.Id} is not a configured EMPTY_STORAGE buffer slot.");
        }

        var expected = ExpectedBufferState(request.TaskType);
        if (bufferSlot.State.Occupancy != expected.Occupancy || bufferSlot.State.Load != expected.Load)
        {
            throw new WorkflowRuleException(
                "EMPTY_BUFFER_STATE_MISMATCH",
                $"{request.TaskType.ToDatabase()} buffer slot must be {expected.Occupancy}/{expected.Load} but was {bufferSlot.State.Occupancy}/{bufferSlot.State.Load}.");
        }

        ValidateAssociations(request, bufferSlot);
    }

    private static void ValidateAssociations(CreateTransportRequest request, SlotSnapshot slot)
    {
        if (request.PartId.HasValue)
        {
            if (slot.KnownPartIds.Count > 0 && !slot.KnownPartIds.Contains(request.PartId.Value))
            {
                throw new WorkflowRuleException("PART_CONFLICT", "Seçilen parça konumun tanımlı parça eşlemesiyle uyumlu değil.");
            }

            if (slot.State.CurrentPartId.HasValue && slot.State.CurrentPartId != request.PartId)
            {
                throw new WorkflowRuleException("PART_CONFLICT", "Seçilen parça konumdaki mevcut parçayla uyumlu değil.");
            }
        }

        if (request.ContainerTypeId.HasValue)
        {
            if (slot.DefaultContainerTypeId.HasValue && slot.DefaultContainerTypeId != request.ContainerTypeId)
            {
                throw new WorkflowRuleException("CONTAINER_CONFLICT", "The requested container conflicts with the slot's source-backed container mapping.");
            }

            if (slot.State.CurrentContainerTypeId.HasValue && slot.State.CurrentContainerTypeId != request.ContainerTypeId)
            {
                throw new WorkflowRuleException("CONTAINER_CONFLICT", "The requested container conflicts with the slot's current known container.");
            }
        }
    }
}
