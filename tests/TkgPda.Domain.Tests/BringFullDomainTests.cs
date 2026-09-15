using TkgPda.Domain;
using Xunit;

namespace TkgPda.Domain.Tests;

/// <summary>
/// BRING_FULL is the inbound half of the model: the warehouse feeds a machine a
/// container already filled with sub-part material. The three original
/// operations only ever modelled the output side, so these tests pin the parts
/// that make reusing one of them unsafe.
/// </summary>
public sealed class BringFullDomainTests
{
    private static CreateTransportRequest Request(
        TransportTaskType taskType,
        bool requiresPreparation = false) => new(
            IdempotencyKey: Guid.NewGuid().ToString("N"),
            Origin: RequestOrigin.WarehouseOperator,
            RequestedBy: Guid.NewGuid(),
            MachineId: Guid.NewGuid(),
            VariantId: Guid.NewGuid(),
            LayoutId: Guid.NewGuid(),
            NavigationProfileId: null,
            ProductGroupId: null,
            PartId: Guid.NewGuid(),
            ContainerTypeId: Guid.NewGuid(),
            TaskType: taskType,
            SourceSlotId: null,
            DestinationSlotId: null,
            RequiresPreparation: requiresPreparation);

    [Fact]
    public void Completion_records_the_delivered_container_as_full()
    {
        var partId = Guid.NewGuid();
        var containerTypeId = Guid.NewGuid();

        var state = TransportRules.CompletionState(TransportTaskType.BringFull, partId, containerTypeId);

        Assert.Equal(OccupancyState.Occupied, state.Occupancy);
        Assert.Equal(LoadState.Full, state.Load);
        Assert.Equal(partId, state.CurrentPartId);
        Assert.Equal(containerTypeId, state.CurrentContainerTypeId);
    }

    [Fact]
    public void Completion_differs_from_bring_empty_on_load_state()
    {
        var partId = Guid.NewGuid();
        var containerTypeId = Guid.NewGuid();

        var full = TransportRules.CompletionState(TransportTaskType.BringFull, partId, containerTypeId);
        var empty = TransportRules.CompletionState(TransportTaskType.BringEmpty, partId, containerTypeId);

        // The whole reason the fourth operation exists: BRING_EMPTY would have
        // recorded a pallet of material as an empty container.
        Assert.Equal(LoadState.Full, full.Load);
        Assert.Equal(LoadState.Empty, empty.Load);
    }

    [Fact]
    public void Machine_position_must_be_clear_before_material_is_requested()
    {
        var expected = TransportRules.ExpectedMachineSlotState(TransportTaskType.BringFull);

        Assert.Equal(OccupancyState.Vacant, expected.Occupancy);
        Assert.Equal(LoadState.Unknown, expected.Load);
    }

    [Theory]
    [InlineData(OccupancyState.Occupied, LoadState.Full)]
    [InlineData(OccupancyState.Occupied, LoadState.Empty)]
    [InlineData(OccupancyState.Unknown, LoadState.Unknown)]
    public void Reservation_is_refused_while_a_container_still_stands_there(
        OccupancyState occupancy, LoadState load)
    {
        var request = Request(TransportTaskType.BringFull);
        var slot = new SlotSnapshot(
            Id: Guid.NewGuid(),
            MachineId: request.MachineId,
            LayoutId: request.LayoutId,
            Role: SlotRole.Staging,
            IsEnabled: true,
            DefaultContainerTypeId: request.ContainerTypeId,
            KnownPartIds: new HashSet<Guid> { request.PartId!.Value },
            State: new SlotState(occupancy, load, null, null));

        var error = Assert.Throws<WorkflowRuleException>(
            () => TransportRules.ValidateReservation(request, slot));
        Assert.Equal("SLOT_STATE_MISMATCH", error.Code);
    }

    [Fact]
    public void Cancellation_leaves_the_position_clear_because_nothing_arrived()
    {
        var current = new SlotState(OccupancyState.Vacant, LoadState.Unknown, null, null);

        var state = TransportRules.CancellationState(TransportTaskType.BringFull, current);

        Assert.Equal(OccupancyState.Vacant, state.Occupancy);
        Assert.Equal(LoadState.Unknown, state.Load);
        Assert.Null(state.CurrentPartId);
    }

    [Fact]
    public void It_never_draws_from_the_empty_container_buffer()
    {
        // Its container comes from a shared preparation area holding material,
        // not from a layout's EMPTY_STORAGE buffer. Claiming a buffer side
        // would let the existing allocator hand it an empty container.
        Assert.False(TransportRules.RequiresBufferSlot(TransportTaskType.BringFull));
        Assert.Null(TransportRules.BufferSlotId(TransportTaskType.BringFull, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TransportRules.ExpectedBufferState(TransportTaskType.BringFull));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TransportRules.BufferCompletionState(TransportTaskType.BringFull, null, null));
    }

    [Fact]
    public void The_machine_position_is_the_destination()
    {
        Assert.True(TransportRules.DeliversToMachine(TransportTaskType.BringFull));
        Assert.True(TransportRules.DeliversToMachine(TransportTaskType.BringEmpty));
        Assert.False(TransportRules.DeliversToMachine(TransportTaskType.RemoveFull));
        Assert.False(TransportRules.DeliversToMachine(TransportTaskType.RemoveEmpty));
    }

    [Fact]
    public void Warehouse_preparation_cannot_be_skipped_by_the_caller()
    {
        // A caller passing false must not make a material feed dispatchable
        // with nothing physically prepared.
        Assert.Equal(
            TransportReadinessState.WaitingPreparation,
            Request(TransportTaskType.BringFull, requiresPreparation: false).InitialReadinessState);
        Assert.Equal(
            TransportReadinessState.WaitingPreparation,
            Request(TransportTaskType.BringFull, requiresPreparation: true).InitialReadinessState);
    }

    [Fact]
    public void The_three_original_operations_keep_their_readiness_behaviour()
    {
        foreach (var taskType in new[]
                 {
                     TransportTaskType.BringEmpty,
                     TransportTaskType.RemoveFull,
                     TransportTaskType.RemoveEmpty
                 })
        {
            Assert.Equal(
                TransportReadinessState.NotRequired,
                Request(taskType).InitialReadinessState);
        }
    }

    [Fact]
    public void Database_code_round_trips()
    {
        Assert.Equal("BRING_FULL", TransportTaskType.BringFull.ToDatabase());
        Assert.Equal(TransportTaskType.BringFull, WorkflowCode.ParseTaskType("BRING_FULL"));
    }

    [Fact]
    public void Every_task_type_has_a_database_code_and_parses_back()
    {
        // Guards the real hazard of widening this enum: a switch arm left out
        // throws at runtime, not at compile time.
        foreach (var taskType in Enum.GetValues<TransportTaskType>())
        {
            var code = taskType.ToDatabase();
            Assert.Equal(taskType, WorkflowCode.ParseTaskType(code));
            Assert.NotNull(TransportRules.CompletionState(taskType, null, null));
            Assert.NotNull(TransportRules.CancellationState(
                taskType, new SlotState(OccupancyState.Vacant, LoadState.Unknown, null, null)));
            _ = TransportRules.ExpectedMachineSlotState(taskType);
            _ = TransportRules.DeliversToMachine(taskType);
        }
    }
}
