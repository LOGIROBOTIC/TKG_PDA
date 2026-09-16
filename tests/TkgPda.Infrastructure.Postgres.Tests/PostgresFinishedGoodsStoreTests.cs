using Npgsql;
using TkgPda.Application;
using TkgPda.Domain;
using TkgPda.Infrastructure.Postgres;
using Xunit;

namespace TkgPda.Infrastructure.Postgres.Tests;

/// <summary>
/// The one link the SQL and HTTP checks cannot reach: the hook that records a
/// delivery when a transport actually completes. Everything here runs against a
/// real database and cleans up after itself.
/// </summary>
public sealed class PostgresFinishedGoodsStoreTests
{
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("TKG_PDA_TEST_CONNECTION_STRING") ?? string.Empty;

    [PostgresFact]
    public async Task Completing_a_removal_puts_the_container_in_the_linked_store_location()
    {
        await ResetAsync();
        var context = await ConfigureAsync();
        var machineSlotId = await LogisticsSlotAsync(context.LayoutId);
        var dropSlotId = await LogisticsSlotAsync(context.LayoutId, code: "TEST-STORE-DROP");
        await SetSlotAsync(machineSlotId, OccupancyState.Occupied, LoadState.Full);
        var locationId = await CreateStoreLocationAsync(capacity: 5);
        await LinkDeliverySlotAsync(dropSlotId, locationId);

        await using var store = new PostgresTransportWorkflowStore(_connectionString);
        var service = new TransportWorkflowService(store);
        var created = await service.CreateAsync(RemoveFull(context, "store-delivery", machineSlotId, dropSlotId));

        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.SentToRds);
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.Running);
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.Completed);

        var locations = await store.ListLocationsAsync();
        var location = Assert.Single(locations, candidate => candidate.Id == locationId);
        var unit = Assert.Single(location.Units);
        Assert.Equal(created.Request.Id, unit.TransportRequestId);
        Assert.Equal(context.MachineId, unit.MachineId);
        Assert.Equal(1, await ScalarAsync<long>(
            "SELECT count(*) FROM ops.store_events WHERE store_location_id = @id AND event_type = 'UNIT_DELIVERED'",
            ("id", locationId)));
    }

    [PostgresFact]
    public async Task A_replayed_completion_does_not_fill_the_store_twice()
    {
        await ResetAsync();
        var context = await ConfigureAsync();
        var machineSlotId = await LogisticsSlotAsync(context.LayoutId);
        var dropSlotId = await LogisticsSlotAsync(context.LayoutId, code: "TEST-STORE-DROP");
        await SetSlotAsync(machineSlotId, OccupancyState.Occupied, LoadState.Full);
        var locationId = await CreateStoreLocationAsync(capacity: 5);
        await LinkDeliverySlotAsync(dropSlotId, locationId);

        await using var store = new PostgresTransportWorkflowStore(_connectionString);
        var service = new TransportWorkflowService(store);
        var created = await service.CreateAsync(RemoveFull(context, "store-replay", machineSlotId, dropSlotId));
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.SentToRds);
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.Running);
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.Completed);

        // Reconciliation re-reports the same terminal status.
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.Completed);

        var locations = await store.ListLocationsAsync();
        Assert.Single(Assert.Single(locations, candidate => candidate.Id == locationId).Units);
    }

    [PostgresFact]
    public async Task An_unlinked_drop_position_leaves_the_store_untouched()
    {
        // Until the customer says which store location a machine's DROP feeds,
        // a completion records nothing rather than guessing a location.
        await ResetAsync();
        var context = await ConfigureAsync();
        var machineSlotId = await LogisticsSlotAsync(context.LayoutId);
        var dropSlotId = await LogisticsSlotAsync(context.LayoutId, code: "TEST-STORE-DROP");
        await SetSlotAsync(machineSlotId, OccupancyState.Occupied, LoadState.Full);
        var locationId = await CreateStoreLocationAsync(capacity: 5);

        await using var store = new PostgresTransportWorkflowStore(_connectionString);
        var service = new TransportWorkflowService(store);
        var created = await service.CreateAsync(RemoveFull(context, "store-unlinked", machineSlotId, dropSlotId));
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.SentToRds);
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.Running);
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.Completed);

        var locations = await store.ListLocationsAsync();
        Assert.Empty(Assert.Single(locations, candidate => candidate.Id == locationId).Units);
    }

    [PostgresFact]
    public async Task Removing_the_last_container_leaves_the_location_empty()
    {
        await ResetAsync();
        var context = await ConfigureAsync();
        var machineSlotId = await LogisticsSlotAsync(context.LayoutId);
        var dropSlotId = await LogisticsSlotAsync(context.LayoutId, code: "TEST-STORE-DROP");
        await SetSlotAsync(machineSlotId, OccupancyState.Occupied, LoadState.Full);
        var locationId = await CreateStoreLocationAsync(capacity: 5);
        await LinkDeliverySlotAsync(dropSlotId, locationId);

        await using var store = new PostgresTransportWorkflowStore(_connectionString);
        var service = new TransportWorkflowService(store);
        var created = await service.CreateAsync(RemoveFull(context, "store-remove", machineSlotId, dropSlotId));
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.SentToRds);
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.Running);
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.Completed);

        var before = Assert.Single(await store.ListLocationsAsync(), c => c.Id == locationId);
        var unit = Assert.Single(before.Units);
        var actor = await EnsureActorAsync();
        var device = await EnsureDeviceAsync();

        var after = await store.RemoveUnitAsync(
            new RemoveStoreUnitCommand(unit.Id, unit.RowVersion, actor, device));

        Assert.True(after.IsEmpty);
        Assert.Equal(1, await ScalarAsync<long>(
            "SELECT count(*) FROM ops.store_events WHERE store_location_id = @id AND event_type = 'UNIT_REMOVED'",
            ("id", locationId)));
    }

    // ---- helpers -----------------------------------------------------------

    private sealed record LayoutContext(Guid MachineId, Guid VariantId, Guid LayoutId);

    private async Task ResetAsync() => await ExecuteAsync(
        """
        TRUNCATE ops.store_events, ops.store_units RESTART IDENTITY CASCADE;
        DELETE FROM ops.store_location_delivery_slots;
        DELETE FROM ops.store_locations WHERE code LIKE 'TEST-%';
        TRUNCATE integration.rds_tasks, ops.transport_request_events, ops.transport_requests,
                 ops.slot_runtime_state, ops.machine_runtime_state RESTART IDENTITY CASCADE;
        DELETE FROM master.layout_slots WHERE code LIKE 'TEST-%';
        """);

    private async Task<LayoutContext> ConfigureAsync()
    {
        var machineId = await ScalarAsync<Guid>("SELECT id FROM master.machines WHERE code = 'SH-01-A'");
        var variantId = await ScalarAsync<Guid>(
            "SELECT id FROM master.variants WHERE machine_id = @machine AND code = '1'", ("machine", machineId));
        var layoutId = await ScalarAsync<Guid>(
            "SELECT layout_id FROM master.variant_layout_assignments WHERE variant_id = @variant LIMIT 1",
            ("variant", variantId));
        await ExecuteAsync(
            """
            INSERT INTO ops.machine_runtime_state (machine_id, active_variant_id, active_layout_id)
            VALUES (@machine, @variant, @layout)
            ON CONFLICT (machine_id) DO UPDATE
            SET active_variant_id = EXCLUDED.active_variant_id, active_layout_id = EXCLUDED.active_layout_id
            """,
            ("machine", machineId), ("variant", variantId), ("layout", layoutId));
        return new LayoutContext(machineId, variantId, layoutId);
    }

    private async Task<Guid> LogisticsSlotAsync(Guid layoutId, string code = "TEST-STORE-SLOT")
    {
        var slotId = await ScalarAsync<Guid>(
            """
            INSERT INTO master.layout_slots (layout_id, code, display_name, slot_role, display_order)
            VALUES (@layout, @code, @code, 'STAGING', 900)
            RETURNING id
            """,
            ("layout", layoutId), ("code", code));
        await ExecuteAsync(
            """
            INSERT INTO ops.slot_runtime_state (layout_slot_id, occupancy_state, load_state)
            VALUES (@slot, 'VACANT', 'UNKNOWN') ON CONFLICT (layout_slot_id) DO NOTHING
            """,
            ("slot", slotId));
        return slotId;
    }

    private Task SetSlotAsync(Guid slotId, OccupancyState occupancy, LoadState load) => ExecuteAsync(
        """
        INSERT INTO ops.slot_runtime_state (layout_slot_id, occupancy_state, load_state)
        VALUES (@slot, @occupancy, @load)
        ON CONFLICT (layout_slot_id) DO UPDATE
        SET occupancy_state = EXCLUDED.occupancy_state, load_state = EXCLUDED.load_state
        """,
        ("slot", slotId), ("occupancy", occupancy.ToDatabase()), ("load", load.ToDatabase()));

    private Task<Guid> CreateStoreLocationAsync(int? capacity) => ScalarAsync<Guid>(
        """
        INSERT INTO ops.store_locations (code, display_name, capacity)
        VALUES ('TEST-STORE-LOC', 'Test Store Location', @capacity)
        RETURNING id
        """,
        ("capacity", capacity));

    private Task LinkDeliverySlotAsync(Guid slotId, Guid locationId) => ExecuteAsync(
        """
        INSERT INTO ops.store_location_delivery_slots (layout_slot_id, store_location_id)
        VALUES (@slot, @location)
        """,
        ("slot", slotId), ("location", locationId));

    private Task<Guid> EnsureActorAsync() => ScalarAsync<Guid>(
        "SELECT id FROM auth.users WHERE is_active ORDER BY created_at LIMIT 1");

    private Task<Guid> EnsureDeviceAsync() => ScalarAsync<Guid>(
        "SELECT id FROM audit.client_devices WHERE is_active ORDER BY device_code LIMIT 1");

    private static CreateTransportRequest RemoveFull(
        LayoutContext context, string key, Guid source, Guid destination) => new(
        key,
        RequestOrigin.LineOperator,
        null,
        context.MachineId,
        context.VariantId,
        context.LayoutId,
        null,
        null,
        null,
        null,
        TransportTaskType.RemoveFull,
        source,
        destination);

    private async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return (T)(await command.ExecuteScalarAsync())!;
    }
}
