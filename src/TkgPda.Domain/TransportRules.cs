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

    private static void ValidateAssociations(CreateTransportRequest request, SlotSnapshot slot)
    {
        if (request.PartId.HasValue)
        {
            if (slot.KnownPartIds.Count > 0 && !slot.KnownPartIds.Contains(request.PartId.Value))
            {
                throw new WorkflowRuleException("PART_CONFLICT", "The requested part conflicts with the slot's source-backed part evidence.");
            }

            if (slot.State.CurrentPartId.HasValue && slot.State.CurrentPartId != request.PartId)
            {
                throw new WorkflowRuleException("PART_CONFLICT", "The requested part conflicts with the slot's current known part.");
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
