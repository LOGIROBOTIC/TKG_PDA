using System.Net;
using System.Net.Http.Json;
using TkgPda.Api;
using TkgPda.Application;
using Xunit;

namespace TkgPda.Api.Tests;

public sealed class Phase08bExecutionApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public async Task Device_bound_operator_reports_load_state_without_changing_occupancy()
    {
        await fixture.ResetAsync();
        var identity = await fixture.SeedIdentityAsync(role: RoleCodes.LineOperator);
        var machine = await fixture.ConfigureMachineAsync();
        var slotId = Assert.Single(await fixture.LogisticsSlotsAsync(machine.LayoutId, 1));
        await fixture.SetSlotAsync(slotId, "OCCUPIED", "EMPTY");
        using var client = await fixture.LoginClientAsync(identity);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/machines/{machine.MachineId}/slots/{slotId}/load-state",
            new UpdateLoadStateHttpRequest("FULL"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<UpdateLoadStateHttpResponse>();
        Assert.NotNull(result);
        Assert.Equal("OCCUPIED", result.OccupancyState);
        Assert.Equal("FULL", result.LoadState);
        Assert.Equal("NOT_CONFIGURED", result.RdsContentSync.Status);

        var context = await client.GetFromJsonAsync<OperationalContextRecord>($"/api/v1/machines/{machine.MachineId}/operational-context");
        var slot = Assert.Single(context!.Slots, item => item.Id == slotId);
        Assert.True(Assert.Single(slot.AvailableActions, item => item.Action == "REMOVE_FULL").Enabled);
        Assert.False(Assert.Single(slot.LoadStateActions, item => item.Action == "MARK_FULL").Enabled);
        Assert.True(Assert.Single(slot.LoadStateActions, item => item.Action == "MARK_EMPTY").Enabled);
        Assert.Equal(1, await fixture.ScalarAsync<long>(
            "SELECT count(*) FROM audit.audit_logs WHERE action = 'LOAD_STATE_REPORTED' AND entity_id = @slot_id",
            ("slot_id", slotId.ToString())));
    }

    [Fact]
    public async Task Load_state_endpoint_rejects_unknown_warehouse_and_active_transport_mutations()
    {
        await fixture.ResetAsync();
        var warehouse = await fixture.SeedIdentityAsync(role: RoleCodes.WarehouseOperator);
        var line = await fixture.SeedIdentityAsync(role: RoleCodes.LineOperator);
        var machine = await fixture.ConfigureMachineAsync();
        var slots = await fixture.LogisticsSlotsAsync(machine.LayoutId, 2);
        await fixture.SetSlotAsync(slots[0], "OCCUPIED", "EMPTY");
        // slots[1] is FULL so the "busy" scaffolding below can use REMOVE_FULL, a real
        // standalone Operator action. It previously used REMOVE_EMPTY, which is
        // internal orchestration vocabulary and is no longer directly requestable.
        await fixture.SetSlotAsync(slots[1], "OCCUPIED", "FULL");

        using var warehouseClient = await fixture.LoginClientAsync(warehouse);
        var forbidden = await warehouseClient.PutAsJsonAsync(
            $"/api/v1/machines/{machine.MachineId}/slots/{slots[0]}/load-state",
            new UpdateLoadStateHttpRequest("FULL"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("OPERATION_NOT_ALLOWED_FOR_ROLE", await ApiTestFixture.ErrorCodeAsync(forbidden));

        using var lineClient = await fixture.LoginClientAsync(line);
        var unknown = await lineClient.PutAsJsonAsync(
            $"/api/v1/machines/{machine.MachineId}/slots/{slots[0]}/load-state",
            new UpdateLoadStateHttpRequest("UNKNOWN"));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("LOAD_STATE_NOT_SELECTABLE", await ApiTestFixture.ErrorCodeAsync(unknown));

        var busyPart = await fixture.PartForSlotAsync(slots[1], "P05-P08BBUSY");
        var created = await fixture.SendTransportAsync(lineClient, new(
            machine.MachineId, machine.VariantId, machine.LayoutId, null, null, busyPart, null,
            "REMOVE_FULL"), "phase08b-load-busy");
        created.EnsureSuccessStatusCode();
        var busy = await lineClient.PutAsJsonAsync(
            $"/api/v1/machines/{machine.MachineId}/slots/{slots[1]}/load-state",
            new UpdateLoadStateHttpRequest("FULL"));
        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        Assert.Equal("SLOT_BUSY", await ApiTestFixture.ErrorCodeAsync(busy));
    }

    [Fact]
    public async Task Callback_key_is_required_and_unknown_task_is_ignored_safely()
    {
        await fixture.ResetAsync();
        using var client = fixture.Client();
        var body = new RdsCallbackHttpRequest(
            "taskDone",
            System.Text.Json.JsonSerializer.SerializeToElement(new { taskRecordId = Guid.NewGuid().ToString() }),
            DateTimeOffset.UtcNow);

        var unauthorized = await client.PostAsJsonAsync("/api/integrations/rds/events", body);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/rds/events")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-TKG-RDS-Key", ApiTestFixture.CallbackKey);
        var accepted = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var result = await accepted.Content.ReadFromJsonAsync<RdsCallbackResult>();
        Assert.NotNull(result);
        Assert.False(result.Mutated);
        Assert.Equal("RDS_TASK_UNKNOWN", result.Code);
    }

    [Fact]
    public async Task Authenticated_task_done_callback_completes_known_request_idempotently()
    {
        await fixture.ResetAsync();
        var identity = await fixture.SeedIdentityAsync(role: RoleCodes.LineOperator);
        var machine = await fixture.ConfigureMachineAsync();
        var slotId = Assert.Single(await fixture.LogisticsSlotsAsync(machine.LayoutId, 1));
        await fixture.SetSlotAsync(slotId, "VACANT", "UNKNOWN");
        using var operatorClient = await fixture.LoginClientAsync(identity);
        var callbackPart = await fixture.PartForSlotAsync(slotId, "P05-P08BCB");
        var createdResponse = await fixture.SendTransportAsync(operatorClient, new(
            machine.MachineId, machine.VariantId, machine.LayoutId, null, null, callbackPart, null,
            "BRING_EMPTY"), "phase08b-callback-done");
        var created = await createdResponse.Content.ReadFromJsonAsync<TransportHttpResponse>();
        Assert.NotNull(created);
        await fixture.ExecuteAsync("UPDATE ops.transport_requests SET status = 'SENT_TO_RDS' WHERE id = @id", ("id", created.Id));
        await fixture.ExecuteAsync(
            """
            INSERT INTO integration.rds_tasks (transport_request_id, rds_task_record_id, rds_task_label, rds_status)
            VALUES (@id, @record_id, 'SIM_TASK', 1000)
            """,
            ("id", created.Id),
            ("record_id", created.Id.ToString()));

        using var callbackClient = fixture.Client();
        async Task<HttpResponseMessage> SendAsync()
        {
            using var callback = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/rds/events")
            {
                Content = JsonContent.Create(new RdsCallbackHttpRequest(
                    "taskDone",
                    System.Text.Json.JsonSerializer.SerializeToElement(new { taskRecordId = created.Id.ToString(), status = 1003, agvId = "sim_01" }),
                    DateTimeOffset.UtcNow))
            };
            callback.Headers.Add("X-TKG-RDS-Key", ApiTestFixture.CallbackKey);
            return await callbackClient.SendAsync(callback);
        }

        Assert.Equal(HttpStatusCode.Accepted, (await SendAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await SendAsync()).StatusCode);
        Assert.Equal("COMPLETED", await fixture.ScalarAsync<string>("SELECT status FROM ops.transport_requests WHERE id = @id", ("id", created.Id)));
        Assert.Equal("OCCUPIED:EMPTY", await fixture.ScalarAsync<string>(
            "SELECT occupancy_state || ':' || load_state FROM ops.slot_runtime_state WHERE layout_slot_id = @slot_id",
            ("slot_id", slotId)));
    }
}
