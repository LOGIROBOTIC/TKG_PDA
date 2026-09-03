using TkgPda.Application;
using TkgPda.Domain;
using Xunit;

namespace TkgPda.Application.Tests;

public sealed class ContainerActionResolutionPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("REMOVE_EMPTY")]
    public void Operator_container_action_parser_rejects_missing_or_unsupported_values(string? value)
    {
        var error = Assert.Throws<WorkflowRuleException>(() => OperatorContainerActionCode.Parse(value));

        Assert.Equal("CONTAINER_ACTION_INVALID", error.Code);
    }

    [Fact]
    public void Kasa_doldu_from_occupied_empty_marks_full_then_removes_full()
    {
        var result = ContainerActionResolutionPolicy.ResolveSingle(
            OperatorContainerAction.Full,
            OccupancyState.Occupied,
            LoadState.Empty);

        Assert.Equal(TransportTaskType.RemoveFull, result.Operation);
        Assert.True(result.MarkFullBeforeCreate);
    }

    [Fact]
    public void Kasa_doldu_from_occupied_full_reuses_remove_full_without_state_rewrite()
    {
        var result = ContainerActionResolutionPolicy.ResolveSingle(
            OperatorContainerAction.Full,
            OccupancyState.Occupied,
            LoadState.Full);

        Assert.Equal(TransportTaskType.RemoveFull, result.Operation);
        Assert.False(result.MarkFullBeforeCreate);
    }

    [Theory]
    [InlineData(OccupancyState.Vacant, LoadState.Unknown)]
    [InlineData(OccupancyState.Unknown, LoadState.Unknown)]
    public void Kasa_doldu_fails_closed_without_an_occupied_container(
        OccupancyState occupancy,
        LoadState load)
    {
        Assert.Throws<WorkflowRuleException>(() =>
            ContainerActionResolutionPolicy.ResolveSingle(OperatorContainerAction.Full, occupancy, load));
    }

    [Fact]
    public void Kasa_bos_from_vacant_unknown_brings_empty()
    {
        var result = ContainerActionResolutionPolicy.ResolveSingle(
            OperatorContainerAction.Empty,
            OccupancyState.Vacant,
            LoadState.Unknown);

        Assert.Equal(TransportTaskType.BringEmpty, result.Operation);
        Assert.False(result.MarkFullBeforeCreate);
    }

    [Theory]
    [InlineData(LoadState.Empty)]
    [InlineData(LoadState.Full)]
    public void Kasa_bos_rejects_occupied_positions(LoadState load)
    {
        var error = Assert.Throws<WorkflowRuleException>(() =>
            ContainerActionResolutionPolicy.ResolveSingle(
                OperatorContainerAction.Empty,
                OccupancyState.Occupied,
                load));

        Assert.Equal("CONTAINER_POSITION_OCCUPIED", error.Code);
    }

    [Theory]
    [InlineData(OccupancyState.Occupied, LoadState.Full, TransportTaskType.RemoveFull)]
    [InlineData(OccupancyState.Occupied, LoadState.Empty, TransportTaskType.RemoveEmpty)]
    public void Remove_all_selects_operation_from_tkg_business_state(
        OccupancyState occupancy,
        LoadState load,
        TransportTaskType expected)
    {
        Assert.Equal(expected, ContainerActionResolutionPolicy.ResolveRemoveAll(occupancy, load));
    }

    [Fact]
    public void Remove_all_ignores_vacant_unknown()
    {
        Assert.Null(ContainerActionResolutionPolicy.ResolveRemoveAll(
            OccupancyState.Vacant,
            LoadState.Unknown));
    }

    [Fact]
    public void Remove_all_blocks_unknown_position()
    {
        var error = Assert.Throws<WorkflowRuleException>(() =>
            ContainerActionResolutionPolicy.ResolveRemoveAll(
                OccupancyState.Unknown,
                LoadState.Unknown));

        Assert.Equal("REMOVE_ALL_POSITION_UNKNOWN", error.Code);
    }
}
