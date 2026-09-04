using System.Text.Json;
using TkgPda.Domain;
using Xunit;

namespace TkgPda.Application.Tests;

public sealed class TransportEtaServiceTests
{
    private readonly TransportEtaService service = new();

    [Fact]
    public void Terminal_requests_return_unavailable_eta()
    {
        var terminalStatuses = new[]
        {
            TransportRequestStatus.Completed,
            TransportRequestStatus.Failed,
            TransportRequestStatus.Cancelled
        };

        foreach (var status in terminalStatuses)
        {
            var projection = service.Project(
                status,
                rdsStatus: null,
                agvId: "sim_01",
                startedAt: DateTimeOffset.UtcNow,
                queuePosition: 1);

            Assert.Equal("UNAVAILABLE", projection.State);
            Assert.Null(projection.Seconds);
        }
    }

    [Theory]
    [InlineData(1001)] // Stopped
    [InlineData(1003)] // Success
    [InlineData(1004)] // Failed
    [InlineData(1005)] // Exception
    [InlineData(1006)] // Cancelled
    public void Terminal_rds_statuses_return_unavailable_eta(int rdsStatus)
    {
        var projection = service.Project(
            TransportRequestStatus.Running,
            rdsStatus,
            agvId: "sim_01",
            startedAt: DateTimeOffset.UtcNow,
            queuePosition: 1);

        Assert.Equal("UNAVAILABLE", projection.State);
        Assert.Null(projection.Seconds);
    }

    [Fact]
    public void Explicit_remaining_time_in_last_status_json_is_used()
    {
        using var doc = JsonDocument.Parse("""{"remaining_time": 47.8}""");

        var projection = service.Project(
            TransportRequestStatus.Running,
            rdsStatus: 1000,
            agvId: "sim_01",
            startedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            queuePosition: 1,
            lastStatusJson: doc.RootElement);

        Assert.Equal("AVAILABLE", projection.State);
        Assert.Equal(48, projection.Seconds);
    }

    [Fact]
    public void Running_request_calculates_remaining_time_from_started_at()
    {
        var now = DateTimeOffset.UtcNow;
        var startedAt = now.AddSeconds(-40); // 40 seconds elapsed

        var projection = service.Project(
            TransportRequestStatus.Running,
            rdsStatus: 1000,
            agvId: "sim_01",
            startedAt: startedAt,
            queuePosition: 1,
            now: now);

        Assert.Equal("AVAILABLE", projection.State);
        // AverageCycleTimeSeconds (120) - 40 = 80 seconds
        Assert.Equal(80, projection.Seconds);
    }

    [Fact]
    public void Running_request_exceeding_cycle_time_preserves_floor()
    {
        var now = DateTimeOffset.UtcNow;
        var startedAt = now.AddSeconds(-200); // 200 seconds elapsed

        var projection = service.Project(
            TransportRequestStatus.Running,
            rdsStatus: 1000,
            agvId: "sim_01",
            startedAt: startedAt,
            queuePosition: 1,
            now: now);

        Assert.Equal("AVAILABLE", projection.State);
        Assert.Equal(15, projection.Seconds); // Minimum floor is 15s
    }

    [Theory]
    [InlineData(1, 120)]
    [InlineData(2, 240)]
    [InlineData(3, 360)]
    public void Queued_requests_project_eta_proportionally_to_position(int queuePosition, int expectedSeconds)
    {
        var projection = service.Project(
            TransportRequestStatus.Queued,
            rdsStatus: null,
            agvId: null,
            startedAt: null,
            queuePosition: queuePosition);

        Assert.Equal("AVAILABLE", projection.State);
        Assert.Equal(expectedSeconds, projection.Seconds);
    }

    [Fact]
    public void From_reliable_remaining_time_handles_valid_and_invalid_values()
    {
        var valid = service.FromReliableRemainingTime(65);
        Assert.Equal("AVAILABLE", valid.State);
        Assert.Equal(65, valid.Seconds);

        var negative = service.FromReliableRemainingTime(-5);
        Assert.Equal("UNAVAILABLE", negative.State);
        Assert.Null(negative.Seconds);

        var nullValue = service.FromReliableRemainingTime(null);
        Assert.Equal("UNAVAILABLE", nullValue.State);
        Assert.Null(nullValue.Seconds);
    }

    [Fact]
    public void Waiting_preparation_yields_unavailable_eta()
    {
        var projection = service.Project(
            TransportRequestStatus.Queued,
            rdsStatus: null,
            agvId: null,
            startedAt: null,
            queuePosition: 1,
            readinessState: TransportReadinessState.WaitingPreparation);

        Assert.Equal("UNAVAILABLE", projection.State);
        Assert.Null(projection.Seconds);
    }
}
