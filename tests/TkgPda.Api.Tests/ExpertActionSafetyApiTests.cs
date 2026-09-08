using System.Net;
using System.Net.Http.Json;
using TkgPda.Api;
using TkgPda.Application;
using Xunit;

namespace TkgPda.Api.Tests;

/// <summary>
/// Expert recovery actions are high-risk physical operations. They must bind to a
/// real TKG TransportRequest, enforce machine authorization, resolve the exact
/// RDS-assigned target server-side, and never accept a caller-supplied vehicle.
/// Previously manual-finish and redo ignored the route requestId entirely and
/// force-finished whatever vehicles the caller named.
/// </summary>
public sealed class ExpertActionSafetyApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public async Task Manual_finish_rejects_an_unknown_request_instead_of_acting_on_caller_supplied_vehicles()
    {
        await fixture.ResetAsync();
        var supervisor = await fixture.SeedIdentityAsync(role: RoleCodes.Supervisor);
        using var client = await fixture.LoginClientAsync(supervisor);

        // A caller-chosen vehicle in the body must not be able to drive anything.
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transport-requests/{Guid.NewGuid()}/manual-finish",
            new { vehicles = new[] { "Sfl-Cbd15" } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("REQUEST_NOT_FOUND", await ApiTestFixture.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Redo_rejects_an_unknown_request()
    {
        await fixture.ResetAsync();
        var supervisor = await fixture.SeedIdentityAsync(role: RoleCodes.Supervisor);
        using var client = await fixture.LoginClientAsync(supervisor);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/transport-requests/{Guid.NewGuid()}/redo",
            new { vehicles = new[] { "Sfl-Cbd15" } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("REQUEST_NOT_FOUND", await ApiTestFixture.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Set_priority_rejects_an_unknown_request()
    {
        await fixture.ResetAsync();
        var identity = await fixture.SeedIdentityAsync(role: RoleCodes.Supervisor);
        using var client = await fixture.LoginClientAsync(identity);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/transport-requests/{Guid.NewGuid()}/priority",
            new { priority = 5 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("REQUEST_NOT_FOUND", await ApiTestFixture.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Line_and_warehouse_operators_are_forbidden_from_changing_priority()
    {
        // Conservative product policy: no repository or customer evidence was found
        // that a Line/Warehouse Operator should reprioritise a transport.
        await fixture.ResetAsync();
        foreach (var role in new[] { RoleCodes.LineOperator, RoleCodes.WarehouseOperator })
        {
            var identity = await fixture.SeedIdentityAsync(role: role);
            using var client = await fixture.LoginClientAsync(identity);
            var response = await client.PostAsJsonAsync(
                $"/api/v1/transport-requests/{Guid.NewGuid()}/priority", new { priority = 5 });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Line_operator_is_forbidden_from_manual_finish_and_redo()
    {
        await fixture.ResetAsync();
        var lineOperator = await fixture.SeedIdentityAsync(role: RoleCodes.LineOperator);
        using var client = await fixture.LoginClientAsync(lineOperator);
        var requestId = Guid.NewGuid();

        var manualFinish = await client.PostAsJsonAsync(
            $"/api/v1/transport-requests/{requestId}/manual-finish", new { });
        var redo = await client.PostAsJsonAsync(
            $"/api/v1/transport-requests/{requestId}/redo", new { });

        Assert.Equal(HttpStatusCode.Forbidden, manualFinish.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, redo.StatusCode);
    }

    [Fact]
    public async Task Expert_actions_enforce_machine_authorization_on_a_real_request()
    {
        await fixture.ResetAsync();
        var context = await fixture.ConfigureMachineAsync();
        var slot = (await fixture.LogisticsSlotsAsync(context.LayoutId, 1)).Single();
        await fixture.SetSlotAsync(slot, "OCCUPIED", "FULL");

        var owner = await fixture.SeedIdentityAsync();
        using var ownerClient = await fixture.LoginClientAsync(owner);
        var created = await CreateRemoveFullAsync(ownerClient, context, slot, "expert-machine-auth");

        // A supervisor with no permission on this machine must be refused.
        var outsider = await fixture.SeedIdentityAsync(role: RoleCodes.Supervisor, canView: false, canOperate: false);
        using var outsiderClient = await fixture.LoginClientAsync(outsider);

        var response = await outsiderClient.PostAsJsonAsync(
            $"/api/v1/transport-requests/{created.Id}/manual-finish", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Set_priority_rejects_an_out_of_range_value_on_a_real_request()
    {
        await fixture.ResetAsync();
        var context = await fixture.ConfigureMachineAsync();
        var slot = (await fixture.LogisticsSlotsAsync(context.LayoutId, 1)).Single();
        await fixture.SetSlotAsync(slot, "OCCUPIED", "FULL");
        var identity = await fixture.SeedIdentityAsync(role: RoleCodes.Supervisor);
        using var client = await fixture.LoginClientAsync(identity);
        var created = await CreateRemoveFullAsync(client, context, slot, "expert-priority-range");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/transport-requests/{created.Id}/priority",
            new { priority = -1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("PRIORITY_OUT_OF_RANGE", await ApiTestFixture.ErrorCodeAsync(response));
    }

    // The API test host runs with the global dispatch gate closed (Rds:DispatchEnabled
    // defaults to false), so this doubles as end-to-end evidence that the gate refuses
    // an execution mutation before any target resolution or RDS call happens.
    [Fact]
    public async Task Expert_action_is_refused_while_the_global_dispatch_gate_is_closed()
    {
        await fixture.ResetAsync();
        var context = await fixture.ConfigureMachineAsync();
        var slot = (await fixture.LogisticsSlotsAsync(context.LayoutId, 1)).Single();
        await fixture.SetSlotAsync(slot, "OCCUPIED", "FULL");
        var identity = await fixture.SeedIdentityAsync(role: RoleCodes.Supervisor);
        using var client = await fixture.LoginClientAsync(identity);
        var created = await CreateRemoveFullAsync(client, context, slot, "expert-no-task");

        // The request exists and the caller is authorized, but the gate is closed, so
        // the mutation is refused before the RDS target is even resolved.
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transport-requests/{created.Id}/priority",
            new { priority = 5 });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("RDS_DISPATCH_DISABLED", await ApiTestFixture.ErrorCodeAsync(response));
    }

    private static async Task<TransportHttpResponse> CreateRemoveFullAsync(
        HttpClient client,
        LayoutContext context,
        Guid source,
        string idempotencyKey)
    {
        var payload = new CreateTransportHttpRequest(
            context.MachineId,
            context.VariantId,
            context.LayoutId,
            null,
            null,
            null,
            null,
            "REMOVE_FULL",
            source,
            null);
        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/api/v1/transport-requests")
            {
                Content = JsonContent.Create(payload),
                Headers = { { ApiHeaders.IdempotencyKey, idempotencyKey } },
            });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TransportHttpResponse>()
            ?? throw new InvalidOperationException("Transport response was empty.");
    }
}
