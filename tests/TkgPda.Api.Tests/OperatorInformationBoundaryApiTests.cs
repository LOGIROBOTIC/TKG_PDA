using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TkgPda.Api;
using TkgPda.Application;
using Xunit;

namespace TkgPda.Api.Tests;

/// <summary>
/// The Operator information boundary must be enforced by the API payload itself,
/// not by the Flutter client declining to render a field. These tests assert on the
/// raw JSON so that a widget-level assertion can never again stand in for a
/// contract-level guarantee.
/// </summary>
public sealed class OperatorInformationBoundaryApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    // Raw RDS technical identifiers that must never reach a Line/Warehouse Operator.
    private static readonly string[] ForbiddenOperatorFields =
    [
        "uuid", "vehicleId", "vehicle_id", "currentMap", "current_map",
        "currentStation", "current_station", "lastStation", "last_station",
        "currentOrderId", "current_order_id", "currentOrderState",
        "blockId", "block_id", "lockInfo", "lock_info", "lockNickName",
        "workSite", "work_site", "siteId", "site_id",
        "x", "y", "angle", "isLoaded", "remainingTime", "agvId", "agv_id"
    ];

    private static void AssertNoForbiddenFields(JsonElement element, string path = "$")
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Assert.False(
                        ForbiddenOperatorFields.Contains(property.Name, StringComparer.OrdinalIgnoreCase),
                        $"Operator payload leaked forbidden RDS field '{property.Name}' at {path}.");
                    AssertNoForbiddenFields(property.Value, $"{path}.{property.Name}");
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    AssertNoForbiddenFields(item, $"{path}[{index++}]");
                }
                break;
        }
    }

    [Fact]
    public async Task Operator_robot_payload_contains_only_business_fields()
    {
        await fixture.ResetAsync();
        // A fully populated robot: every raw technical field is present upstream.
        fixture.RdsClient.RobotAlarms = new(
            [new RdsRobotAlarmSnapshot("AGV-01", "error", "E1", "a real fault", null, 1)],
            [
                new RdsRobotStateSnapshot(
                    RobotId: "AGV-01",
                    Model: "HELIFORK",
                    Dispatchable: true,
                    UndispatchableReason: null,
                    BatteryLevel: 0.88,
                    IsCharging: false,
                    CurrentStation: "PP80",
                    LastStation: "LM12",
                    CurrentMap: "26.7.2026_map_optimak",
                    LockInfo: new RdsCoreRobotLockInfo(true, "10.0.0.5", "TKG_LOCK", 8088, "1788522696", 1, null),
                    ActiveOrderId: "ORD-999",
                    ActiveOrderState: "RUNNING",
                    RdsReportedPayloadLoaded: true,
                    Online: true,
                    Identity: new RdsRobotIdentity("ROBOT-INTERNAL-123", "AGV-01"))
            ]);

        var identity = await fixture.SeedIdentityAsync();
        using var client = await fixture.LoginClientAsync(identity);
        var response = await client.GetAsync("/api/v1/rds/robots");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        AssertNoForbiddenFields(document.RootElement);

        // The specific values must be absent too, not merely renamed.
        Assert.DoesNotContain("ROBOT-INTERNAL-123", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AGV-01", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PP80", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LM12", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("26.7.2026_map_optimak", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ORD-999", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TKG_LOCK", json, StringComparison.OrdinalIgnoreCase);

        // ...while the business fields the Operator UX genuinely renders survive.
        var robot = document.RootElement.GetProperty("robots").EnumerateArray().Single();
        Assert.Equal("HELIFORK", robot.GetProperty("displayName").GetString());
        Assert.True(robot.GetProperty("online").GetBoolean());
        Assert.Equal(0.88, robot.GetProperty("batteryLevel").GetDouble(), 3);
        Assert.True(robot.GetProperty("hasActiveAlarm").GetBoolean());
    }

    [Fact]
    public async Task Operator_robot_fault_flag_ignores_notices_and_warnings()
    {
        await fixture.ResetAsync();
        fixture.RdsClient.RobotAlarms = new(
            [
                new RdsRobotAlarmSnapshot("AGV-02", "notice", "80004", "no joining order", null, 1),
                new RdsRobotAlarmSnapshot("AGV-02", "warning", "W1", "a warning", null, 1)
            ],
            [new RdsRobotStateSnapshot("AGV-02", "HELIFORK", true, null, Online: true)]);

        var identity = await fixture.SeedIdentityAsync();
        using var client = await fixture.LoginClientAsync(identity);
        var response = await client.GetAsync("/api/v1/rds/robots");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var robot = document.RootElement.GetProperty("robots").EnumerateArray().Single();
        Assert.False(robot.GetProperty("hasActiveAlarm").GetBoolean());
    }

    [Fact]
    public async Task Operator_queue_payload_does_not_carry_the_raw_agv_identifier()
    {
        await fixture.ResetAsync();
        var context = await fixture.ConfigureMachineAsync();
        var slot = (await fixture.LogisticsSlotsAsync(context.LayoutId, 1)).Single();
        await fixture.SetSlotAsync(slot, "OCCUPIED", "FULL");
        var identity = await fixture.SeedIdentityAsync();
        using var client = await fixture.LoginClientAsync(identity);

        var payload = new CreateTransportHttpRequest(
            context.MachineId,
            context.VariantId,
            context.LayoutId,
            null,
            null,
            null,
            null,
            "REMOVE_FULL",
            slot,
            null);
        using var create = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/api/v1/transport-requests")
            {
                Content = JsonContent.Create(payload),
                Headers = { { ApiHeaders.IdempotencyKey, "boundary-queue-agv" } },
            });
        create.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/v1/transport-requests/queue?terminal=false");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        AssertNoForbiddenFields(document.RootElement);
    }

    [Fact]
    public async Task Raw_rds_order_passthrough_is_no_longer_reachable_by_an_operator()
    {
        await fixture.ResetAsync();
        var identity = await fixture.SeedIdentityAsync();
        using var client = await fixture.LoginClientAsync(identity);

        var response = await client.GetAsync("/api/v1/rds/orders");

        // The unfiltered fleet-wide RDSCore passthrough has been removed entirely.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
