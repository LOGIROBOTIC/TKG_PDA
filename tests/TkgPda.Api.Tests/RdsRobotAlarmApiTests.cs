using System.Net;
using System.Net.Http.Json;
using TkgPda.Application;
using Xunit;

namespace TkgPda.Api.Tests;

// Customer Monitoring — Robot Alarm Visibility:
// Admin gets the full current-alarm detail (GET /api/v1/admin/rds/alarms);
// Operator gets only a coarse fleet-wide signal (GET /api/v1/rds/robot-alarm-summary)
// with no per-robot or diagnostic detail leaked.
public sealed class RdsRobotAlarmApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    public async Task InitializeAsync() => await fixture.ResetAsync();
    public async Task DisposeAsync() => await fixture.ResetAsync();

    [Fact]
    public async Task Admin_alarm_endpoint_returns_detailed_projection()
    {
        fixture.RdsClient.RobotAlarms = new RdsRobotAlarmReport(
            [new RdsRobotAlarmSnapshot("sim_01", "fatal", "F1", "Fatal one", "2026-08-30T10:00:00Z", 3)],
            [new RdsRobotStateSnapshot("sim_01", "AMB-150", false, "low_battery")]);
        var admin = await fixture.SeedIdentityAsync(RoleCodes.Admin, machineCode: null);
        var client = await fixture.AdminLoginClientAsync(admin);

        var response = await client.GetAsync("/api/v1/admin/rds/alarms");
        response.EnsureSuccessStatusCode();
        var alarms = await response.Content.ReadFromJsonAsync<List<RdsRobotAlarmAdminRecord>>();

        var alarm = Assert.Single(alarms!);
        Assert.Equal("sim_01", alarm.RobotId);
        Assert.Equal("AMB-150", alarm.RobotModel);
        Assert.Equal("fatal", alarm.Severity);
        Assert.Equal("F1", alarm.Code);
        Assert.Equal("Fatal one", alarm.Description);
        Assert.Equal(3, alarm.OccurrenceCount);
        Assert.Equal("2026-08-30T10:00:00Z", alarm.OccurredAt);
        Assert.False(alarm.Dispatchable);
        Assert.Equal("low_battery", alarm.UndispatchableReason);
    }

    [Fact]
    public async Task Non_admin_is_forbidden_from_the_admin_alarm_endpoint()
    {
        var identity = await fixture.SeedIdentityAsync();
        var client = await fixture.LoginClientAsync(identity);

        var response = await client.GetAsync("/api/v1/admin/rds/alarms");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("ROLE_NOT_AUTHORIZED", await ApiTestFixture.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Admin_alarm_endpoint_surfaces_rds_unavailable_instead_of_empty_list()
    {
        fixture.RdsClient.RobotAlarmsUnavailable = true;
        var admin = await fixture.SeedIdentityAsync(RoleCodes.Admin, machineCode: null);
        var client = await fixture.AdminLoginClientAsync(admin);

        var response = await client.GetAsync("/api/v1/admin/rds/alarms");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("RDS_UNAVAILABLE", await ApiTestFixture.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Operator_summary_reports_no_active_alarm_when_snapshot_is_empty()
    {
        var identity = await fixture.SeedIdentityAsync();
        var client = await fixture.LoginClientAsync(identity);

        var response = await client.GetAsync("/api/v1/rds/robot-alarm-summary");
        response.EnsureSuccessStatusCode();
        var summary = await response.Content.ReadFromJsonAsync<RdsRobotAlarmOperatorSummary>();

        Assert.False(summary!.HasActiveRobotAlarm);
        Assert.Null(summary.HighestSeverity);
        Assert.Equal(0, summary.ActiveAlarmCount);
    }

    [Fact]
    public async Task Operator_summary_reports_highest_severity_without_leaking_diagnostic_detail()
    {
        fixture.RdsClient.RobotAlarms = new RdsRobotAlarmReport(
            [
                new RdsRobotAlarmSnapshot("sim_01", "warning", "W1", "Warning one", "t1", 1),
                new RdsRobotAlarmSnapshot("sim_02", "fatal", "F1", "Fatal one", "t2", 1),
            ],
            []);
        var identity = await fixture.SeedIdentityAsync();
        var client = await fixture.LoginClientAsync(identity);

        var response = await client.GetAsync("/api/v1/rds/robot-alarm-summary");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var summary = await response.Content.ReadFromJsonAsync<RdsRobotAlarmOperatorSummary>();

        Assert.True(summary!.HasActiveRobotAlarm);
        Assert.Equal("fatal", summary.HighestSeverity);
        Assert.Equal(2, summary.ActiveAlarmCount);
        Assert.DoesNotContain("sim_01", body, StringComparison.Ordinal);
        Assert.DoesNotContain("robotId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"code\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Operator_summary_surfaces_rds_unavailable_instead_of_empty_summary()
    {
        fixture.RdsClient.RobotAlarmsUnavailable = true;
        var identity = await fixture.SeedIdentityAsync();
        var client = await fixture.LoginClientAsync(identity);

        var response = await client.GetAsync("/api/v1/rds/robot-alarm-summary");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("RDS_UNAVAILABLE", await ApiTestFixture.ErrorCodeAsync(response));
    }
}
