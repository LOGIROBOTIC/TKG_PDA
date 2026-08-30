using TkgPda.Domain;
using Xunit;

namespace TkgPda.Domain.Tests;

// Customer Workflow — Empty Container Buffer: BRING_EMPTY draws from, and
// REMOVE_EMPTY returns to, a configured EMPTY_STORAGE slot. REMOVE_FULL never
// touches the buffer. These rules are orthogonal to the reserved (machine)
// slot rules in TransportDomainTests.
public sealed class TransportEmptyContainerBufferDomainTests
{
    [Theory]
    [InlineData(TransportTaskType.BringEmpty, true)]
    [InlineData(TransportTaskType.RemoveEmpty, true)]
    [InlineData(TransportTaskType.RemoveFull, false)]
    public void Only_empty_container_operations_require_a_buffer_slot(
        TransportTaskType taskType,
        bool expected)
    {
        Assert.Equal(expected, TransportRules.RequiresBufferSlot(taskType));
    }

    [Fact]
    public void Bring_empty_buffer_slot_is_its_source()
    {
        var sourceId = Guid.NewGuid();
        Assert.Equal(
            sourceId,
            TransportRules.BufferSlotId(TransportTaskType.BringEmpty, sourceId, null));
    }

    [Fact]
    public void Remove_empty_buffer_slot_is_its_destination()
    {
        var destinationId = Guid.NewGuid();
        Assert.Equal(
            destinationId,
            TransportRules.BufferSlotId(TransportTaskType.RemoveEmpty, null, destinationId));
    }

    [Fact]
    public void Remove_full_never_has_a_buffer_slot()
    {
        Assert.Null(TransportRules.BufferSlotId(TransportTaskType.RemoveFull, Guid.NewGuid(), Guid.NewGuid()));
    }

    [Theory]
    [InlineData(TransportTaskType.BringEmpty, OccupancyState.Occupied, LoadState.Empty)]
    [InlineData(TransportTaskType.RemoveEmpty, OccupancyState.Vacant, LoadState.Unknown)]
    public void Expected_buffer_state_matches_the_operation(
        TransportTaskType taskType,
        OccupancyState expectedOccupancy,
        LoadState expectedLoad)
    {
        var expected = TransportRules.ExpectedBufferState(taskType);

        Assert.Equal(expectedOccupancy, expected.Occupancy);
        Assert.Equal(expectedLoad, expected.Load);
    }

    [Theory]
    [InlineData(TransportTaskType.BringEmpty, OccupancyState.Vacant, LoadState.Unknown)]
    [InlineData(TransportTaskType.RemoveEmpty, OccupancyState.Occupied, LoadState.Empty)]
    public void Buffer_completion_state_is_the_mirror_image_of_the_machine_side(
        TransportTaskType taskType,
        OccupancyState expectedOccupancy,
        LoadState expectedLoad)
    {
        var completed = TransportRules.BufferCompletionState(taskType, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(expectedOccupancy, completed.Occupancy);
        Assert.Equal(expectedLoad, completed.Load);
    }

    [Theory]
    [InlineData(TransportTaskType.BringEmpty, OccupancyState.Occupied, LoadState.Empty)]
    [InlineData(TransportTaskType.RemoveEmpty, OccupancyState.Vacant, LoadState.Unknown)]
    public void A_correctly_configured_and_staged_buffer_slot_is_accepted(
        TransportTaskType taskType,
        OccupancyState occupancy,
        LoadState load)
    {
        var request = Request(taskType);
        var buffer = BufferSlot(occupancy, load) with { Role = SlotRole.EmptyStorage };

        TransportRules.ValidateBufferSlot(request, buffer);
    }

    [Theory]
    [InlineData(TransportTaskType.BringEmpty, OccupancyState.Occupied, LoadState.Empty)]
    [InlineData(TransportTaskType.RemoveEmpty, OccupancyState.Vacant, LoadState.Unknown)]
    public void A_slot_that_is_not_configured_as_EMPTY_STORAGE_is_rejected(
        TransportTaskType taskType,
        OccupancyState occupancy,
        LoadState load)
    {
        var request = Request(taskType);
        var buffer = BufferSlot(occupancy, load) with { Role = SlotRole.Staging };

        var error = Assert.Throws<WorkflowRuleException>(() => TransportRules.ValidateBufferSlot(request, buffer));

        Assert.Equal("EMPTY_BUFFER_SLOT_NOT_CONFIGURED", error.Code);
    }

    [Theory]
    [InlineData(TransportTaskType.BringEmpty, OccupancyState.Vacant, LoadState.Unknown)]
    [InlineData(TransportTaskType.RemoveEmpty, OccupancyState.Occupied, LoadState.Empty)]
    public void A_buffer_slot_in_the_wrong_state_is_rejected(
        TransportTaskType taskType,
        OccupancyState wrongOccupancy,
        LoadState wrongLoad)
    {
        var request = Request(taskType);
        var buffer = BufferSlot(wrongOccupancy, wrongLoad) with { Role = SlotRole.EmptyStorage };

        var error = Assert.Throws<WorkflowRuleException>(() => TransportRules.ValidateBufferSlot(request, buffer));

        Assert.Equal("EMPTY_BUFFER_STATE_MISMATCH", error.Code);
    }

    [Fact]
    public void A_configured_buffer_slot_still_enforces_part_associations()
    {
        var request = Request(TransportTaskType.BringEmpty) with { PartId = Guid.NewGuid() };
        var buffer = BufferSlot(OccupancyState.Occupied, LoadState.Empty) with
        {
            Role = SlotRole.EmptyStorage,
            KnownPartIds = new HashSet<Guid> { Guid.NewGuid() },
        };

        var error = Assert.Throws<WorkflowRuleException>(() => TransportRules.ValidateBufferSlot(request, buffer));

        Assert.Equal("PART_CONFLICT", error.Code);
    }

    private static CreateTransportRequest Request(TransportTaskType taskType)
    {
        Guid? source = taskType == TransportTaskType.BringEmpty ? null : Guid.NewGuid();
        Guid? destination = taskType == TransportTaskType.BringEmpty ? Guid.NewGuid() : null;
        return new CreateTransportRequest(
            "test-key",
            RequestOrigin.LineOperator,
            null,
            MachineId,
            VariantId,
            LayoutId,
            null,
            null,
            null,
            null,
            taskType,
            source,
            destination);
    }

    private static SlotSnapshot BufferSlot(OccupancyState occupancy, LoadState load) => new(
        Guid.NewGuid(),
        MachineId,
        LayoutId,
        SlotRole.Other,
        true,
        null,
        new HashSet<Guid>(),
        new SlotState(occupancy, load, null, null));

    private static readonly Guid MachineId = Guid.NewGuid();
    private static readonly Guid VariantId = Guid.NewGuid();
    private static readonly Guid LayoutId = Guid.NewGuid();
}
