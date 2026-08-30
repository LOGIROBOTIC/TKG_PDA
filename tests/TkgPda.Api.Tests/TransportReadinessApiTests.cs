using System.Net;
using System.Net.Http.Json;
using TkgPda.Api;
using Xunit;

namespace TkgPda.Api.Tests;

// WP-05A — Warehouse Preparation Readiness: business readiness is orthogonal
// to the execution state machine. These tests exercise the HTTP contract for
// requiresPreparation on create/batch and the POST .../ready action.
public sealed class TransportReadinessApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public async Task Omitted_requires_preparation_creates_a_not_required_request()
    {
        await fixture.ResetAsync();
        var context = await fixture.ConfigureMachineAsync();
        var slot = (await fixture.LogisticsSlotsAsync(context.LayoutId, 1)).Single();
        await fixture.SetSlotAsync(slot, "VACANT", "UNKNOWN");
        var identity = await fixture.SeedIdentityAsync();
        using var client = await fixture.LoginClientAsync(identity);

        var response = await fixture.SendTransportAsync(client, Bring(context, slot), "readiness-omitted");
        var created = await response.Content.ReadFromJsonAsync<TransportHttpResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("NOT_REQUIRED", created!.ReadinessState);
        Assert.Null(created.ReadyAt);
    }

    [Fact]
    public async Task Requires_preparation_false_creates_a_not_required_request()
    {
        await fixture.ResetAsync();
        var context = await fixture.ConfigureMachineAsync();
        var slot = (await fixture.LogisticsSlotsAsync(context.LayoutId, 1)).Single();
        await fixture.SetSlotAsync(slot, "VACANT", "UNKNOWN");
        var identity = await fixture.SeedIdentityAsync();
        using var client = await fixture.LoginClientAsync(identity);

        var response = await fixture.SendTransportAsync(client, Bring(context, slot, requiresPreparation: false), "readiness-false");
        var created = await response.Content.ReadFromJsonAsync<TransportHttpResponse>();

        Assert.Equal("NOT_REQUIRED", created!.ReadinessState);
    }

    [Fact]
    public async Task Requires_preparation_true_creates_a_waiting_preparation_request()
    {
        await fixture.ResetAsync();
        var context = await fixture.ConfigureMachineAsync();
        var slot = (await fixture.LogisticsSlotsAsync(context.LayoutId, 1)).Single();
        await fixture.SetSlotAsync(slot, "VACANT", "UNKNOWN");
        var identity = await fixture.SeedIdentityAsync();
        using var client = await fixture.LoginClientAsync(identity);

        var response = await fixture.SendTransportAsync(client, Bring(context, slot, requiresPreparation: true), "readiness-true");
        var created = await response.Content.ReadFromJsonAsync<TransportHttpResponse>();

        Assert.Equal("WAITING_PREPARATION", created!.ReadinessState);
        Assert.Null(created.ReadyAt);
    }

    [Fact]
    public async Task Mark_ready_transitions_waiting_preparation_to_ready_and_is_idempotent()
    {
        await fixture.ResetAsync();
        var context = await fixture.ConfigureMachineAsync();
        var slot = (await fixture.LogisticsSlotsAsync(context.LayoutId, 1)).Single();
        await fixture.SetSlotAsync(slot, "VACANT", "UNKNOWN");
        var identity = await fixture.SeedIdentityAsync();
        using var client = await fixture.LoginClientAsync(identity);
        var createdResponse = await fixture.SendTransportAsync(client, Bring(context, slot, requiresPreparation: true), "mark-ready-http");
        var created = await createdResponse.Content.ReadFromJsonAsync<TransportHttpResponse>();

        var first = await client.PostAsync($"/api/v1/transport-requests/{created!.Id}/ready", null);
        var firstBody = await first.Content.ReadFromJsonAsync<TransportHttpResponse>();
        var second = await client.PostAsync($"/api/v1/transport-requests/{created.Id}/ready", null);
        var secondBody = await second.Content.ReadFromJsonAsync<TransportHttpResponse>();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("READY", firstBody!.ReadinessState);
        Assert.NotNull(firstBody.ReadyAt);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("READY", secondBody!.ReadinessState);
        // The replay returns the row read back from Postgres (microsecond
        // precision) while the first response serialized the in-memory
        // DateTimeOffset.UtcNow (100ns ticks) it just wrote — tolerate that,
        // don't require bit-for-bit equality.
        Assert.True(
            (firstBody.ReadyAt!.Value - secondBody.ReadyAt!.Value).Duration() < TimeSpan.FromMilliseconds(1),
            $"Expected readyAt to stay stable across repeated mark-ready calls: {firstBody.ReadyAt:O} vs {secondBody.ReadyAt:O}.");
    }

    [Fact]
    public async Task Mark_ready_on_a_not_required_request_is_rejected()
    {
        await fixture.ResetAsync();
        var context = await fixture.ConfigureMachineAsync();
        var slot = (await fixture.LogisticsSlotsAsync(context.LayoutId, 1)).Single();
        await fixture.SetSlotAsync(slot, "VACANT", "UNKNOWN");
        var identity = await fixture.SeedIdentityAsync();
        using var client = await fixture.LoginClientAsync(identity);
        var createdResponse = await fixture.SendTransportAsync(client, Bring(context, slot), "not-required-ready-http");
        var created = await createdResponse.Content.ReadFromJsonAsync<TransportHttpResponse>();

        var response = await client.PostAsync($"/api/v1/transport-requests/{created!.Id}/ready", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("READINESS_NOT_APPLICABLE", await ApiTestFixture.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Mark_ready_on_a_terminal_request_is_rejected()
    {
        await fixture.ResetAsync();
        var context = await fixture.ConfigureMachineAsync();
        var slot = (await fixture.LogisticsSlotsAsync(context.LayoutId, 1)).Single();
        await fixture.SetSlotAsync(slot, "VACANT", "UNKNOWN");
        var identity = await fixture.SeedIdentityAsync();
        using var client = await fixture.LoginClientAsync(identity);
        var createdResponse = await fixture.SendTransportAsync(client, Bring(context, slot, requiresPreparation: true), "terminal-ready-http");
        var created = await createdResponse.Content.ReadFromJsonAsync<TransportHttpResponse>();
        var cancelled = await client.PostAsync($"/api/v1/transport-requests/{created!.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

        var response = await client.PostAsync($"/api/v1/transport-requests/{created.Id}/ready", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("REQUEST_TERMINAL", await ApiTestFixture.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Batch_children_independently_preserve_their_own_readiness()
    {
        await fixture.ResetAsync();
        var context = await fixture.ConfigureMachineAsync();
        var slots = await fixture.LogisticsSlotsAsync(context.LayoutId, 2);
        await fixture.SetSlotAsync(slots[0], "VACANT", "UNKNOWN");
        await fixture.SetSlotAsync(slots[1], "VACANT", "UNKNOWN");
        var identity = await fixture.SeedIdentityAsync();
        using var client = await fixture.LoginClientAsync(identity);

        var response = await client.PostAsJsonAsync(
            "/api/v1/transport-batches",
            new CreateTransportBatchHttpRequest(
                "batch-mixed-readiness",
                context.MachineId,
                [
                    BatchItem(context, slots[0], "batch-readiness-waiting", requiresPreparation: true),
                    BatchItem(context, slots[1], "batch-readiness-not-required", requiresPreparation: false)
                ]));
        var batch = await response.Content.ReadFromJsonAsync<TransportBatchHttpResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("WAITING_PREPARATION", batch!.Items[0].ReadinessState);
        Assert.Equal("NOT_REQUIRED", batch.Items[1].ReadinessState);
    }

    [Fact]
    public async Task Batch_atomicity_is_intact_when_one_item_is_invalid_regardless_of_mixed_readiness()
    {
        await fixture.ResetAsync();
        var context = await fixture.ConfigureMachineAsync();
        var slots = await fixture.LogisticsSlotsAsync(context.LayoutId, 2);
        await fixture.SetSlotAsync(slots[0], "VACANT", "UNKNOWN");
        // slots[1] stays OCCUPIED/FULL-incompatible with BRING_EMPTY -> invalid child.
        await fixture.SetSlotAsync(slots[1], "OCCUPIED", "FULL");
        var identity = await fixture.SeedIdentityAsync();
        using var client = await fixture.LoginClientAsync(identity);

        var response = await client.PostAsJsonAsync(
            "/api/v1/transport-batches",
            new CreateTransportBatchHttpRequest(
                "batch-mixed-readiness-invalid",
                context.MachineId,
                [
                    BatchItem(context, slots[0], "batch-atomic-waiting", requiresPreparation: true),
                    BatchItem(context, slots[1], "batch-atomic-invalid", requiresPreparation: false)
                ]));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("SLOT_STATE_MISMATCH", await ApiTestFixture.ErrorCodeAsync(response));
        Assert.Equal(0L, await fixture.ScalarAsync<long>("SELECT count(*) FROM ops.transport_batches"));
        Assert.Equal(0L, await fixture.ScalarAsync<long>("SELECT count(*) FROM ops.transport_requests"));
    }

    private static CreateTransportHttpRequest Bring(LayoutContext context, Guid destination, bool? requiresPreparation = null) =>
        requiresPreparation is null
            ? new CreateTransportHttpRequest(context.MachineId, context.VariantId, context.LayoutId, null, null, null, null, "BRING_EMPTY", null, destination)
            : new CreateTransportHttpRequest(context.MachineId, context.VariantId, context.LayoutId, null, null, null, null, "BRING_EMPTY", null, destination, RequiresPreparation: requiresPreparation.Value);

    private static CreateTransportBatchItemHttpRequest BatchItem(LayoutContext context, Guid destination, string idempotencyKey, bool requiresPreparation) => new(
        idempotencyKey,
        context.MachineId,
        context.VariantId,
        context.LayoutId,
        null,
        null,
        null,
        null,
        "BRING_EMPTY",
        null,
        destination,
        RequiresPreparation: requiresPreparation);
}
