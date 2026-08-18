using Npgsql;
using TkgPda.Application;
using TkgPda.Domain;
using TkgPda.Infrastructure.Postgres;
using Xunit;

namespace TkgPda.Infrastructure.Postgres.Tests;

public sealed class PostgresTransportWorkflowTests
{
    private readonly string _connectionString = Environment.GetEnvironmentVariable("TKG_PDA_TEST_CONNECTION_STRING") ?? string.Empty;

    [PostgresFact]
    public async Task Bring_empty_queues_with_optional_warehouse_source_and_does_not_change_slot_state()
    {
        await ResetAsync();
        var context = await ConfigureAsync("SH-01-A", "1");
        var slotId = await LogisticsSlotAsync(context.LayoutId, requireContainer: false);
        await SetSlotAsync(slotId, OccupancyState.Vacant, LoadState.Unknown);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);

        var result = await store.CreateAndQueueAsync(Command(context, TransportTaskType.BringEmpty, "bring-optional-source", destination: slotId));
        var state = await ReadSlotStateAsync(slotId);

        Assert.Equal(TransportRequestStatus.Queued, result.Request.Status);
        Assert.Null(result.Request.SourceSlotId);
        Assert.Equal((OccupancyState.Vacant, LoadState.Unknown), state);
        Assert.Equal(3, await ScalarAsync<long>("SELECT count(*) FROM ops.transport_request_events WHERE transport_request_id = @id", ("id", result.Request.Id)));
    }

    [PostgresFact]
    public async Task Bring_empty_success_changes_destination_to_occupied_empty()
    {
        await ResetAsync();
        var context = await ConfigureAsync("SH-01-A", "1");
        var slotId = await LogisticsSlotAsync(context.LayoutId, requireContainer: false);
        await SetSlotAsync(slotId, OccupancyState.Vacant, LoadState.Unknown);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);
        var service = new TransportWorkflowService(store);
        var created = await service.CreateAsync(Command(context, TransportTaskType.BringEmpty, "bring-complete", destination: slotId));

        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.SentToRds);
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.Running);
        var completed = await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.Completed);

        Assert.Equal(TransportRequestStatus.Completed, completed.Status);
        Assert.Equal((OccupancyState.Occupied, LoadState.Empty), await ReadSlotStateAsync(slotId));
    }

    [PostgresFact]
    public async Task Remove_full_cancellation_restores_occupied_full_and_preserves_known_values()
    {
        await ResetAsync();
        var context = await ConfigureAsync("SH-01-A", "1");
        var slotId = await LogisticsSlotAsync(context.LayoutId, requireContainer: false);
        var partId = await ScalarAsync<Guid>("SELECT id FROM master.parts ORDER BY part_code LIMIT 1");
        var containerId = await ScalarAsync<Guid>("SELECT id FROM master.container_types ORDER BY code LIMIT 1");
        await SetSlotAsync(slotId, OccupancyState.Occupied, LoadState.Full, partId, containerId);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);
        var created = await store.CreateAndQueueAsync(Command(context, TransportTaskType.RemoveFull, "remove-full-cancel", source: slotId));

        var cancelled = await store.CancelAsync(created.Request.Id, actorUserId: null);
        var row = await ReadFullSlotStateAsync(slotId);

        Assert.Equal(TransportRequestStatus.Cancelled, cancelled.Request.Status);
        Assert.Equal(OccupancyState.Occupied, row.Occupancy);
        Assert.Equal(LoadState.Full, row.Load);
        Assert.Equal(partId, row.PartId);
        Assert.Equal(containerId, row.ContainerId);
        Assert.Equal(
            "QUEUED>CANCELLED",
            await ScalarAsync<string>(
                "SELECT old_status || '>' || new_status FROM ops.transport_request_events WHERE transport_request_id = @id AND event_type = 'REQUEST_CANCELLED_AND_RESERVATION_RELEASED'",
                ("id", created.Request.Id)));
    }

    [PostgresFact]
    public async Task Remove_empty_success_changes_source_to_vacant_unknown()
    {
        await ResetAsync();
        var context = await ConfigureAsync("SH-01-A", "1");
        var slotId = await LogisticsSlotAsync(context.LayoutId, requireContainer: false);
        await SetSlotAsync(slotId, OccupancyState.Occupied, LoadState.Empty);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);
        var service = new TransportWorkflowService(store);
        var created = await service.CreateAsync(Command(context, TransportTaskType.RemoveEmpty, "remove-empty-complete", source: slotId));

        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.SentToRds);
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.Running);
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.Completed);

        Assert.Equal((OccupancyState.Vacant, LoadState.Unknown), await ReadSlotStateAsync(slotId));
    }

    [PostgresFact]
    public async Task Bring_and_remove_empty_cancellation_restore_their_expected_states()
    {
        await ResetAsync();
        var context = await ConfigureAsync("SH-01-A", "1");
        var slots = await LogisticsSlotsAsync(context.LayoutId, 2);
        await SetSlotAsync(slots[0], OccupancyState.Vacant, LoadState.Unknown);
        await SetSlotAsync(slots[1], OccupancyState.Occupied, LoadState.Empty);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);

        var bring = await store.CreateAndQueueAsync(Command(context, TransportTaskType.BringEmpty, "bring-cancel", destination: slots[0]));
        await store.CancelAsync(bring.Request.Id, null);
        var remove = await store.CreateAndQueueAsync(Command(context, TransportTaskType.RemoveEmpty, "remove-empty-cancel", source: slots[1]));
        await store.CancelAsync(remove.Request.Id, null);

        Assert.Equal((OccupancyState.Vacant, LoadState.Unknown), await ReadSlotStateAsync(slots[0]));
        Assert.Equal((OccupancyState.Occupied, LoadState.Empty), await ReadSlotStateAsync(slots[1]));
    }

    [PostgresFact]
    public async Task Machine_region_is_rejected()
    {
        await ResetAsync();
        var context = await ConfigureAsync("SH-01-A", "1");
        var machineRegion = await ScalarAsync<Guid>(
            "SELECT id FROM master.layout_slots WHERE layout_id = @layout_id AND slot_role = 'MACHINE' LIMIT 1",
            ("layout_id", context.LayoutId));
        await SetSlotAsync(machineRegion, OccupancyState.Vacant, LoadState.Unknown);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);

        var error = await Assert.ThrowsAsync<WorkflowRuleException>(() =>
            store.CreateAndQueueAsync(Command(context, TransportTaskType.BringEmpty, "machine-region", destination: machineRegion)));

        Assert.Equal("MACHINE_REGION_FORBIDDEN", error.Code);
    }

    [PostgresFact]
    public async Task Layout_machine_regions_are_distinct_from_customer_machine_master_records()
    {
        await ResetAsync();

        var machineMasterCount = await ScalarAsync<long>("SELECT count(*) FROM master.machines");
        var machineRegionCount = await ScalarAsync<long>("SELECT count(*) FROM master.layout_slots WHERE slot_role = 'MACHINE'");

        Assert.Equal(9, machineMasterCount);
        Assert.Equal(11, machineRegionCount);
    }

    [PostgresFact]
    public async Task Slot_from_another_machine_is_rejected()
    {
        await ResetAsync();
        var context = await ConfigureAsync("SH-01-A", "1");
        var other = await ContextAsync("SH-01-B", "1");
        var otherSlot = await LogisticsSlotAsync(other.LayoutId, requireContainer: false);
        await SetSlotAsync(otherSlot, OccupancyState.Vacant, LoadState.Unknown);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);

        var error = await Assert.ThrowsAsync<WorkflowRuleException>(() =>
            store.CreateAndQueueAsync(Command(context, TransportTaskType.BringEmpty, "cross-machine", destination: otherSlot)));

        Assert.Equal("CROSS_MACHINE_SLOT", error.Code);
    }

    [PostgresFact]
    public async Task Request_context_must_match_active_variant_and_layout()
    {
        await ResetAsync();
        var active = await ConfigureAsync("SH-01-A", "1");
        var inactive = await ContextAsync("SH-01-A", "2");
        var slotId = await LogisticsSlotAsync(active.LayoutId, requireContainer: false);
        await SetSlotAsync(slotId, OccupancyState.Vacant, LoadState.Unknown);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);

        var error = await Assert.ThrowsAsync<WorkflowRuleException>(() =>
            store.CreateAndQueueAsync(Command(inactive, TransportTaskType.BringEmpty, "inactive-context", destination: slotId)));

        Assert.Equal("RUNTIME_CONTEXT_MISMATCH", error.Code);
    }

    [PostgresFact]
    public async Task Sequential_idempotent_retry_returns_the_same_request()
    {
        await ResetAsync();
        var context = await ConfigureAsync("SH-01-A", "1");
        var slotId = await LogisticsSlotAsync(context.LayoutId, requireContainer: false);
        await SetSlotAsync(slotId, OccupancyState.Vacant, LoadState.Unknown);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);
        var command = Command(context, TransportTaskType.BringEmpty, "same-retry", destination: slotId);

        var first = await store.CreateAndQueueAsync(command);
        var second = await store.CreateAndQueueAsync(command);

        Assert.False(first.WasIdempotentReplay);
        Assert.True(second.WasIdempotentReplay);
        Assert.Equal(first.Request.Id, second.Request.Id);
        Assert.Equal(1, await ScalarAsync<long>("SELECT count(*) FROM ops.transport_requests"));
    }

    [PostgresFact]
    public async Task Reusing_an_idempotency_key_for_a_different_payload_is_rejected()
    {
        await ResetAsync();
        var context = await ConfigureAsync("SH-01-A", "1");
        var slots = await LogisticsSlotsAsync(context.LayoutId, 2);
        await SetSlotAsync(slots[0], OccupancyState.Vacant, LoadState.Unknown);
        await SetSlotAsync(slots[1], OccupancyState.Vacant, LoadState.Unknown);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);
        await store.CreateAndQueueAsync(Command(context, TransportTaskType.BringEmpty, "reused-key", destination: slots[0]));

        var error = await Assert.ThrowsAsync<WorkflowRuleException>(() =>
            store.CreateAndQueueAsync(Command(context, TransportTaskType.BringEmpty, "reused-key", destination: slots[1])));

        Assert.Equal("IDEMPOTENCY_KEY_REUSED", error.Code);
    }

    [PostgresFact]
    public async Task Concurrent_requests_for_the_same_slot_create_only_one_reservation()
    {
        await ResetAsync();
        var context = await ConfigureAsync("SH-01-A", "1");
        var slotId = await LogisticsSlotAsync(context.LayoutId, requireContainer: false);
        await SetSlotAsync(slotId, OccupancyState.Vacant, LoadState.Unknown);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);

        var attempts = await Task.WhenAll(
            CaptureAsync(() => store.CreateAndQueueAsync(Command(context, TransportTaskType.BringEmpty, "slot-race-a", destination: slotId))),
            CaptureAsync(() => store.CreateAndQueueAsync(Command(context, TransportTaskType.BringEmpty, "slot-race-b", destination: slotId))));

        Assert.Single(attempts, attempt => attempt.Result is not null);
        var failure = Assert.Single(attempts, attempt => attempt.Error is not null).Error!;
        Assert.Equal("SLOT_ALREADY_RESERVED", failure.Code);
        Assert.Equal(1, await ScalarAsync<long>("SELECT count(*) FROM ops.transport_requests"));
    }

    [PostgresFact]
    public async Task Concurrent_retry_with_same_key_returns_one_logical_request()
    {
        await ResetAsync();
        var context = await ConfigureAsync("SH-01-A", "1");
        var slotId = await LogisticsSlotAsync(context.LayoutId, requireContainer: false);
        await SetSlotAsync(slotId, OccupancyState.Vacant, LoadState.Unknown);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);
        var command = Command(context, TransportTaskType.BringEmpty, "same-key-race", destination: slotId);

        var results = await Task.WhenAll(store.CreateAndQueueAsync(command), store.CreateAndQueueAsync(command));

        Assert.Equal(results[0].Request.Id, results[1].Request.Id);
        Assert.Single(results, result => result.WasIdempotentReplay);
        Assert.Equal(1, await ScalarAsync<long>("SELECT count(*) FROM ops.transport_requests"));
    }

    [PostgresFact]
    public async Task Explicit_container_conflict_is_rejected_but_null_association_is_allowed()
    {
        await ResetAsync();
        var context = await ConfigureAsync("412B", "1");
        var slotId = await LogisticsSlotAsync(context.LayoutId, requireContainer: true);
        await SetSlotAsync(slotId, OccupancyState.Vacant, LoadState.Unknown);
        var defaultContainer = await ScalarAsync<Guid>("SELECT default_container_type_id FROM master.layout_slots WHERE id = @id", ("id", slotId));
        var otherContainer = await ScalarAsync<Guid>("SELECT id FROM master.container_types WHERE id <> @id ORDER BY code LIMIT 1", ("id", defaultContainer));
        await using var store = new PostgresTransportWorkflowStore(_connectionString);

        var conflict = await Assert.ThrowsAsync<WorkflowRuleException>(() =>
            store.CreateAndQueueAsync(Command(context, TransportTaskType.BringEmpty, "container-conflict", destination: slotId) with { ContainerTypeId = otherContainer }));
        Assert.Equal("CONTAINER_CONFLICT", conflict.Code);

        var allowed = await store.CreateAndQueueAsync(Command(context, TransportTaskType.BringEmpty, "container-unresolved", destination: slotId));
        Assert.Equal(TransportRequestStatus.Queued, allowed.Request.Status);
    }

    [PostgresFact]
    public async Task Source_unresolved_associations_accept_existing_master_part_and_container_without_guessing()
    {
        await ResetAsync();
        var context = await ConfigureAsync("GAR", "1");
        var slotId = await ScalarAsync<Guid>(
            """
            SELECT id
            FROM master.layout_slots
            WHERE layout_id = @layout_id
              AND slot_role <> 'MACHINE'
              AND default_container_type_id IS NULL
              AND jsonb_array_length(COALESCE(metadata_json->'canonical_part_codes', '[]'::jsonb)) = 0
            ORDER BY display_order
            LIMIT 1
            """,
            ("layout_id", context.LayoutId));
        var partId = await ScalarAsync<Guid>("SELECT id FROM master.parts ORDER BY part_code LIMIT 1");
        var containerId = await ScalarAsync<Guid>("SELECT id FROM master.container_types ORDER BY code LIMIT 1");
        await SetSlotAsync(slotId, OccupancyState.Vacant, LoadState.Unknown);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);
        var command = Command(context, TransportTaskType.BringEmpty, "unresolved-evidence", destination: slotId) with
        {
            PartId = partId,
            ContainerTypeId = containerId
        };

        var result = await store.CreateAndQueueAsync(command);

        Assert.Equal(TransportRequestStatus.Queued, result.Request.Status);
        Assert.Equal(partId, result.Request.PartId);
        Assert.Equal(containerId, result.Request.ContainerTypeId);
    }

    [PostgresFact]
    public async Task Explicit_part_conflict_from_layout_evidence_is_rejected()
    {
        await ResetAsync();
        var context = await ConfigureAsync("412B", "1");
        var slotId = await ScalarAsync<Guid>(
            """
            SELECT id
            FROM master.layout_slots
            WHERE layout_id = @layout_id
              AND jsonb_array_length(COALESCE(metadata_json->'canonical_part_codes', '[]'::jsonb)) > 0
            ORDER BY display_order
            LIMIT 1
            """,
            ("layout_id", context.LayoutId));
        var knownPartCode = await ScalarAsync<string>(
            "SELECT metadata_json->'canonical_part_codes'->>0 FROM master.layout_slots WHERE id = @id",
            ("id", slotId));
        var conflictingPart = await ScalarAsync<Guid>(
            "SELECT id FROM master.parts WHERE part_code <> @code ORDER BY part_code LIMIT 1",
            ("code", knownPartCode));
        await SetSlotAsync(slotId, OccupancyState.Vacant, LoadState.Unknown);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);

        var error = await Assert.ThrowsAsync<WorkflowRuleException>(() =>
            store.CreateAndQueueAsync(Command(context, TransportTaskType.BringEmpty, "part-conflict", destination: slotId) with { PartId = conflictingPart }));

        Assert.Equal("PART_CONFLICT", error.Code);
    }

    [PostgresFact]
    public async Task Active_reservation_blocks_variant_switch_until_cancelled()
    {
        await ResetAsync();
        var active = await ConfigureAsync("SH-01-A", "1");
        var next = await ContextAsync("SH-01-A", "2");
        var slotId = await LogisticsSlotAsync(active.LayoutId, requireContainer: false);
        await SetSlotAsync(slotId, OccupancyState.Vacant, LoadState.Unknown);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);
        var created = await store.CreateAndQueueAsync(Command(active, TransportTaskType.BringEmpty, "switch-block", destination: slotId));
        var selection = new VariantSelection(active.MachineId, next.VariantId, next.LayoutId, null, null);

        var blocked = await Assert.ThrowsAsync<WorkflowRuleException>(() => store.SwitchVariantAsync(selection));
        Assert.Equal("ACTIVE_TRANSPORT_EXISTS", blocked.Code);

        await store.CancelAsync(created.Request.Id, null);
        await store.SwitchVariantAsync(selection);
        var selectedVariant = await ScalarAsync<Guid>("SELECT active_variant_id FROM ops.machine_runtime_state WHERE machine_id = @id", ("id", active.MachineId));
        Assert.Equal(next.VariantId, selectedVariant);
    }

    [PostgresFact]
    public async Task Cancellation_after_dispatch_uses_the_stable_deferred_error()
    {
        await ResetAsync();
        var context = await ConfigureAsync("SH-01-A", "1");
        var slotId = await LogisticsSlotAsync(context.LayoutId, requireContainer: false);
        await SetSlotAsync(slotId, OccupancyState.Vacant, LoadState.Unknown);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);
        var service = new TransportWorkflowService(store);
        var created = await service.CreateAsync(Command(context, TransportTaskType.BringEmpty, "late-cancel", destination: slotId));
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.SentToRds);

        var error = await Assert.ThrowsAsync<WorkflowRuleException>(() => service.CancelAsync(created.Request.Id, null));

        Assert.Equal(WorkflowErrorCodes.CancellationAfterExecutionDeferred, error.Code);
    }

    [PostgresFact]
    public async Task Cancellation_while_running_uses_the_stable_deferred_error()
    {
        await ResetAsync();
        var context = await ConfigureAsync("SH-01-A", "1");
        var slotId = await LogisticsSlotAsync(context.LayoutId, requireContainer: false);
        await SetSlotAsync(slotId, OccupancyState.Vacant, LoadState.Unknown);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);
        var service = new TransportWorkflowService(store);
        var created = await service.CreateAsync(Command(context, TransportTaskType.BringEmpty, "running-cancel", destination: slotId));
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.SentToRds);
        await service.RecordExecutionTransitionAsync(created.Request.Id, TransportRequestStatus.Running);

        var error = await Assert.ThrowsAsync<WorkflowRuleException>(() => service.CancelAsync(created.Request.Id, null));

        Assert.Equal(WorkflowErrorCodes.CancellationAfterExecutionDeferred, error.Code);
    }

    [PostgresFact]
    public async Task Concurrent_cancellation_and_dispatch_do_not_deadlock()
    {
        await ResetAsync();
        var context = await ConfigureAsync("SH-01-A", "1");
        var slotId = await LogisticsSlotAsync(context.LayoutId, requireContainer: false);
        await SetSlotAsync(slotId, OccupancyState.Vacant, LoadState.Unknown);
        await using var store = new PostgresTransportWorkflowStore(_connectionString);
        var service = new TransportWorkflowService(store);
        var created = await service.CreateAsync(Command(context, TransportTaskType.BringEmpty, "cancel-dispatch-race", destination: slotId));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var outcomes = await Task.WhenAll(
            CaptureRuleAsync(async () => _ = await service.CancelAsync(created.Request.Id, null, timeout.Token)),
            CaptureRuleAsync(async () => _ = await service.RecordExecutionTransitionAsync(
                created.Request.Id,
                TransportRequestStatus.SentToRds,
                timeout.Token)));

        Assert.Single(outcomes, error => error is null);
        var conflict = Assert.Single(outcomes, error => error is not null)!;
        Assert.Contains(
            conflict.Code,
            new[] { "ILLEGAL_STATUS_TRANSITION", WorkflowErrorCodes.CancellationAfterExecutionDeferred });
    }

    private async Task ResetAsync()
    {
        await ExecuteAsync(
            "TRUNCATE integration.rds_tasks, ops.transport_request_events, ops.transport_requests, ops.slot_runtime_state, ops.machine_runtime_state RESTART IDENTITY CASCADE");
    }

    private async Task<LayoutContext> ConfigureAsync(string machineCode, string variantCode)
    {
        var context = await ContextAsync(machineCode, variantCode);
        await ExecuteAsync(
            """
            INSERT INTO ops.machine_runtime_state (machine_id, active_variant_id, active_layout_id, active_navigation_id)
            VALUES (@machine_id, @variant_id, @layout_id, NULL)
            ON CONFLICT (machine_id) DO UPDATE
            SET active_variant_id = EXCLUDED.active_variant_id,
                active_layout_id = EXCLUDED.active_layout_id,
                active_navigation_id = NULL,
                row_version = ops.machine_runtime_state.row_version + 1,
                updated_at = now()
            """,
            ("machine_id", context.MachineId),
            ("variant_id", context.VariantId),
            ("layout_id", context.LayoutId));
        return context;
    }

    private async Task<LayoutContext> ContextAsync(string machineCode, string variantCode)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT m.id, v.id, l.id
            FROM master.machines m
            JOIN master.variants v ON v.machine_id = m.id AND v.code = @variant_code
            JOIN master.variant_layout_assignments a ON a.variant_id = v.id AND a.machine_id = m.id
            JOIN master.layouts l ON l.id = a.layout_id AND l.machine_id = m.id
            WHERE m.code = @machine_code AND a.is_default
            """,
            connection);
        command.Parameters.AddWithValue("machine_code", machineCode);
        command.Parameters.AddWithValue("variant_code", variantCode);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"Missing layout context for {machineCode} V{variantCode}.");
        return new LayoutContext(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2));
    }

    private async Task<Guid> LogisticsSlotAsync(Guid layoutId, bool requireContainer)
    {
        var condition = requireContainer ? "AND default_container_type_id IS NOT NULL" : string.Empty;
        return await ScalarAsync<Guid>(
            $"SELECT id FROM master.layout_slots WHERE layout_id = @layout_id AND slot_role <> 'MACHINE' {condition} ORDER BY display_order, code LIMIT 1",
            ("layout_id", layoutId));
    }

    private async Task<Guid[]> LogisticsSlotsAsync(Guid layoutId, int count)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id FROM master.layout_slots WHERE layout_id = @layout_id AND slot_role <> 'MACHINE' ORDER BY display_order, code LIMIT @count",
            connection);
        command.Parameters.AddWithValue("layout_id", layoutId);
        command.Parameters.AddWithValue("count", count);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<Guid>();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetGuid(0));
        }

        Assert.Equal(count, result.Count);
        return [.. result];
    }

    private Task SetSlotAsync(
        Guid slotId,
        OccupancyState occupancy,
        LoadState load,
        Guid? partId = null,
        Guid? containerId = null) => ExecuteAsync(
        """
        INSERT INTO ops.slot_runtime_state (
            layout_slot_id, occupancy_state, load_state, current_part_id, current_container_type_id)
        VALUES (@slot_id, @occupancy, @load, @part_id, @container_id)
        ON CONFLICT (layout_slot_id) DO UPDATE
        SET occupancy_state = EXCLUDED.occupancy_state,
            load_state = EXCLUDED.load_state,
            current_part_id = EXCLUDED.current_part_id,
            current_container_type_id = EXCLUDED.current_container_type_id,
            row_version = ops.slot_runtime_state.row_version + 1,
            updated_at = now()
        """,
        ("slot_id", slotId),
        ("occupancy", occupancy.ToDatabase()),
        ("load", load.ToDatabase()),
        ("part_id", partId),
        ("container_id", containerId));

    private async Task<(OccupancyState Occupancy, LoadState Load)> ReadSlotStateAsync(Guid slotId)
    {
        var state = await ReadFullSlotStateAsync(slotId);
        return (state.Occupancy, state.Load);
    }

    private async Task<(OccupancyState Occupancy, LoadState Load, Guid? PartId, Guid? ContainerId)> ReadFullSlotStateAsync(Guid slotId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT occupancy_state, load_state, current_part_id, current_container_type_id FROM ops.slot_runtime_state WHERE layout_slot_id = @id",
            connection);
        command.Parameters.AddWithValue("id", slotId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            WorkflowCode.ParseOccupancy(reader.GetString(0)),
            WorkflowCode.ParseLoad(reader.GetString(1)),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3));
    }

    private static CreateTransportRequest Command(
        LayoutContext context,
        TransportTaskType type,
        string key,
        Guid? source = null,
        Guid? destination = null) => new(
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
        type,
        source,
        destination);

    private static async Task<Attempt> CaptureAsync(Func<Task<CreateTransportResult>> action)
    {
        try
        {
            return new Attempt(await action(), null);
        }
        catch (WorkflowRuleException error)
        {
            return new Attempt(null, error);
        }
    }

    private static async Task<WorkflowRuleException?> CaptureRuleAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (WorkflowRuleException error)
        {
            return error;
        }
    }

    private async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        AddParameters(command, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        AddParameters(command, parameters);
        var value = await command.ExecuteScalarAsync();
        return value is T typed ? typed : throw new InvalidOperationException($"Scalar query did not return {typeof(T).Name}.");
    }

    private static void AddParameters(NpgsqlCommand command, IEnumerable<(string Name, object? Value)> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    private sealed record LayoutContext(Guid MachineId, Guid VariantId, Guid LayoutId);
    private sealed record Attempt(CreateTransportResult? Result, WorkflowRuleException? Error);
}
