using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using TkgPda.Application;
using TkgPda.Domain;

namespace TkgPda.Infrastructure.Postgres;

public sealed class PostgresRdsExecutionStore(NpgsqlDataSource dataSource) : IRdsExecutionStore
{
    private const string RequestSelect =
        """
        SELECT r.id, r.request_no, COALESCE(r.idempotency_key, ''), r.status, tt.code, r.request_origin,
               r.requested_by, r.machine_id, r.variant_id, r.layout_id, r.navigation_id,
               r.product_group_id, r.part_id, r.container_type_id, r.source_slot_id,
               r.destination_slot_id, r.priority, r.readiness_state, r.ready_at
        FROM ops.transport_requests r
        JOIN master.task_types tt ON tt.id = r.task_type_id
        """;

    private const string TaskProjectionSelect =
        """
        SELECT rt.transport_request_id, rt.rds_task_record_id, rt.rds_task_label,
               rt.rds_status, rt.agv_id, rt.out_order_no, rt.ended_reason,
               rt.request_payload_json::text, rt.response_payload_json::text,
               rt.last_status_json::text,
               (rt.response_payload_json ? 'cancellationRequestedAt') AS cancellation_requested,
               tr.status
        FROM integration.rds_tasks rt
        JOIN ops.transport_requests tr ON tr.id = rt.transport_request_id
        """;

    public async Task<IReadOnlyList<Guid>> ListQueuedRequestIdsAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id
            FROM ops.transport_requests
            WHERE status = 'QUEUED'
              AND readiness_state <> 'WAITING_PREPARATION'
            ORDER BY priority, queued_at, id
            LIMIT @limit
            """,
            connection);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 100));
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
        return ids;
    }

    public async Task<IReadOnlyList<RdsTaskProjection>> ListActiveTasksAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            TaskProjectionSelect +
            " WHERE tr.status IN ('SENT_TO_RDS','RUNNING') ORDER BY rt.last_sync_at NULLS FIRST, rt.id LIMIT @limit",
            connection);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500));
        var rows = new List<RdsTaskProjection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadTaskProjection(reader));
        return rows;
    }

    public async Task<IReadOnlyList<Guid>> ListPendingWorkSiteRefreshRequestIdsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT rt.transport_request_id
            FROM integration.rds_tasks rt
            JOIN ops.transport_requests tr ON tr.id = rt.transport_request_id
            WHERE rt.rds_status IN (1001, 1003, 1004, 1005, 1006)
              AND tr.navigation_id IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1
                  FROM audit.audit_logs a
                  WHERE a.entity_type = 'TRANSPORT_REQUEST'
                    AND a.entity_id = rt.transport_request_id::text
                    AND a.action IN ('RDS_WORKSITE_REFRESHED', 'RDS_WORKSITE_REFRESH_SKIPPED'))
            ORDER BY rt.last_sync_at NULLS FIRST, rt.transport_request_id
            LIMIT @limit
            """,
            connection);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500));
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
        return ids;
    }

    public async Task<IRdsExecutionLease?> TryAcquireRequestLeaseAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(
                "SELECT pg_try_advisory_lock(hashtextextended(@key, 0))",
                connection);
            command.Parameters.AddWithValue("key", $"tkg:rds:{requestId:D}");
            var acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
            return acquired ? new PostgresRdsExecutionLease(connection, requestId) : null;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<RdsDispatchContext?> GetDispatchContextAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            SELECT r.id, r.request_no, COALESCE(r.idempotency_key, ''), r.status, tt.code, r.request_origin,
                   r.requested_by, r.machine_id, r.variant_id, r.layout_id, r.navigation_id,
                   r.product_group_id, r.part_id, r.container_type_id, r.source_slot_id,
                   r.destination_slot_id, r.priority, r.readiness_state, r.ready_at,
                   m.code, v.code, main_part.part_code, p.part_code, ct.code,
                   template.id, template.rds_task_label, template.priority, template.config_json::text,
                   src.slot_id, src.slot_code, src.slot_group_code, src.slot_role, src.site_id,
                   src.rds_type, src.last_filled, src.last_locked, src.last_disabled,
                   src.last_sync_failed, src.last_sync_at,
                   dst.slot_id, dst.slot_code, dst.slot_group_code, dst.slot_role, dst.site_id,
                   dst.rds_type, dst.last_filled, dst.last_locked, dst.last_disabled,
                   dst.last_sync_failed, dst.last_sync_at
            FROM ops.transport_requests r
            JOIN master.task_types tt ON tt.id = r.task_type_id
            JOIN master.machines m ON m.id = r.machine_id
            JOIN master.variants v ON v.id = r.variant_id
            LEFT JOIN master.product_groups pg ON pg.id = r.product_group_id
            LEFT JOIN master.parts main_part ON main_part.id = pg.main_part_id
            LEFT JOIN master.parts p ON p.id = r.part_id
            LEFT JOIN master.container_types ct ON ct.id = r.container_type_id
            LEFT JOIN LATERAL (
                SELECT t.*
                FROM master.task_templates t
                WHERE t.variant_id = r.variant_id
                  AND t.task_type_id = r.task_type_id
                  AND t.is_active
                  AND (t.product_group_id = r.product_group_id OR t.product_group_id IS NULL)
                ORDER BY (t.product_group_id = r.product_group_id) DESC, t.id
                LIMIT 1
            ) template ON true
            LEFT JOIN LATERAL (
                SELECT ls.id AS slot_id, ls.code AS slot_code, sg.code AS slot_group_code,
                       ls.slot_role, rw.rds_site_id AS site_id, rw.rds_type, rw.last_filled,
                       rw.last_locked, rw.last_disabled, rw.last_sync_failed, rw.last_sync_at
                FROM master.layout_slots ls
                LEFT JOIN master.slot_group_members sgm ON sgm.layout_slot_id = ls.id
                LEFT JOIN master.slot_groups sg ON sg.id = sgm.slot_group_id
                LEFT JOIN integration.layout_slot_rds_mapping map ON map.layout_slot_id = ls.id
                LEFT JOIN integration.rds_worksites rw ON rw.id = map.rds_worksite_id
                WHERE (r.source_slot_id IS NOT NULL AND ls.id = r.source_slot_id)
                   OR (r.source_slot_id IS NULL AND template.source_slot_group_id IS NOT NULL
                       AND sgm.slot_group_id = template.source_slot_group_id AND ls.layout_id = r.layout_id)
                ORDER BY CASE WHEN ls.id = r.source_slot_id THEN 0 ELSE 1 END,
                         sgm.priority, ls.display_order, ls.id
                LIMIT 1
            ) src ON true
            LEFT JOIN LATERAL (
                SELECT ls.id AS slot_id, ls.code AS slot_code, sg.code AS slot_group_code,
                       ls.slot_role, rw.rds_site_id AS site_id, rw.rds_type, rw.last_filled,
                       rw.last_locked, rw.last_disabled, rw.last_sync_failed, rw.last_sync_at
                FROM master.layout_slots ls
                LEFT JOIN master.slot_group_members sgm ON sgm.layout_slot_id = ls.id
                LEFT JOIN master.slot_groups sg ON sg.id = sgm.slot_group_id
                LEFT JOIN integration.layout_slot_rds_mapping map ON map.layout_slot_id = ls.id
                LEFT JOIN integration.rds_worksites rw ON rw.id = map.rds_worksite_id
                WHERE (r.destination_slot_id IS NOT NULL AND ls.id = r.destination_slot_id)
                   OR (r.destination_slot_id IS NULL AND template.destination_slot_group_id IS NOT NULL
                       AND sgm.slot_group_id = template.destination_slot_group_id AND ls.layout_id = r.layout_id)
                ORDER BY CASE WHEN ls.id = r.destination_slot_id THEN 0 ELSE 1 END,
                         sgm.priority, ls.display_order, ls.id
                LIMIT 1
            ) dst ON true
            WHERE r.id = @request_id
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("request_id", requestId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var request = ReadRequest(reader);
        if (reader.IsDBNull(24))
        {
            throw new RdsExecutionException("RDS_TASK_TEMPLATE_NOT_FOUND", "No active task template matches the request variant, product group and operation.");
        }

        return new RdsDispatchContext(
            request,
            reader.GetString(19),
            reader.GetString(20),
            Text(reader, 21),
            Text(reader, 22),
            Text(reader, 23),
            reader.GetGuid(24),
            Text(reader, 25),
            reader.GetInt32(26),
            Json(reader.GetString(27)),
            Endpoint(reader, 28),
            Endpoint(reader, 39));
    }

    public async Task<TransportRequestSnapshot?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(RequestSelect + " WHERE r.id = @id", connection);
        command.Parameters.AddWithValue("id", requestId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRequest(reader) : null;
    }

    public Task<RdsTaskProjection?> FindTaskByRequestAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        FindTaskAsync("rt.transport_request_id = @value", requestId, cancellationToken);

    public Task<RdsTaskProjection?> FindTaskByRecordIdAsync(string taskRecordId, CancellationToken cancellationToken = default) =>
        FindTaskAsync("rt.rds_task_record_id = @value", taskRecordId, cancellationToken);

    public async Task EnsureTaskRecordAsync(Guid requestId, string taskRecordId, string taskLabel, JsonElement requestPayload, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO integration.rds_tasks (
                             transport_request_id, rds_task_record_id, rds_task_label, request_payload_json)
                         VALUES (@request_id, @record_id, @label, @payload)
                         ON CONFLICT (transport_request_id) DO NOTHING
                         """,
                         connection,
                         transaction))
        {
            insert.Parameters.AddWithValue("request_id", requestId);
            insert.Parameters.AddWithValue("record_id", taskRecordId);
            insert.Parameters.AddWithValue("label", taskLabel);
            AddJson(insert, "payload", requestPayload);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var verify = new NpgsqlCommand(
                         "SELECT rds_task_record_id, rds_task_label FROM integration.rds_tasks WHERE transport_request_id = @request_id FOR UPDATE",
                         connection,
                         transaction))
        {
            verify.Parameters.AddWithValue("request_id", requestId);
            await using var reader = await verify.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)
                || !string.Equals(reader.GetString(0), taskRecordId, StringComparison.Ordinal)
                || !string.Equals(reader.GetString(1), taskLabel, StringComparison.Ordinal))
            {
                throw new RdsExecutionException("RDS_TASK_IDENTITY_CONFLICT", "The request already owns a different RDS task identity or label.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public Task RecordDispatchResponseAsync(Guid requestId, JsonElement responsePayload, DateTimeOffset syncedAt, CancellationToken cancellationToken = default) =>
        UpdateJsonAsync(
            """
            UPDATE integration.rds_tasks
            SET response_payload_json = @json, last_sync_at = @at, updated_at = now()
            WHERE transport_request_id = @request_id
            """,
            requestId,
            responsePayload,
            syncedAt,
            cancellationToken);

    public async Task RecordTaskStatusAsync(Guid requestId, RdsWindTaskSnapshot task, DateTimeOffset syncedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE integration.rds_tasks
            SET rds_task_label = COALESCE(@label, rds_task_label),
                out_order_no = COALESCE(@out_order_no, out_order_no),
                agv_id = COALESCE(@agv_id, agv_id),
                rds_status = @status,
                ended_reason = COALESCE(@ended_reason, ended_reason),
                last_status_json = @raw,
                sent_at = COALESCE(sent_at, @synced_at),
                started_at = CASE WHEN @status = 1000 THEN COALESCE(started_at, @synced_at) ELSE started_at END,
                ended_at = CASE WHEN @status IN (1001,1003,1004,1005,1006) THEN COALESCE(ended_at, @synced_at) ELSE ended_at END,
                last_sync_at = @synced_at,
                updated_at = now()
            WHERE transport_request_id = @request_id
            """,
            connection);
        command.Parameters.AddWithValue("request_id", requestId);
        AddNullableText(command, "label", task.TaskLabel);
        AddNullableText(command, "out_order_no", task.OutOrderNo);
        AddNullableText(command, "agv_id", task.AgvId);
        command.Parameters.AddWithValue("status", (object?)task.Status ?? DBNull.Value);
        AddNullableText(command, "ended_reason", task.EndedReason);
        AddJson(command, "raw", task.RawState);
        command.Parameters.AddWithValue("synced_at", syncedAt);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new RdsExecutionException("RDS_TASK_NOT_FOUND", "The RDS task projection does not exist.");
    }

    public async Task MarkCancellationRequestedAsync(Guid requestId, JsonElement responsePayload, DateTimeOffset requestedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE integration.rds_tasks
            SET response_payload_json = response_payload_json || jsonb_build_object(
                    'cancellationRequestedAt', @requested_at,
                    'stopResponse', @response),
                updated_at = now()
            WHERE transport_request_id = @request_id
            """,
            connection);
        command.Parameters.AddWithValue("request_id", requestId);
        command.Parameters.AddWithValue("requested_at", requestedAt);
        AddJson(command, "response", responsePayload);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new RdsExecutionException("RDS_TASK_NOT_FOUND", "The RDS task projection does not exist.");
    }

    public async Task WriteExecutionAuditAsync(
        Guid requestId,
        string action,
        string code,
        string message,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO audit.audit_logs (
                action, entity_type, entity_id, after_json, correlation_id)
            VALUES (
                @action, 'TRANSPORT_REQUEST', @entity_id,
                jsonb_build_object('code', @code, 'message', @message),
                @correlation_id)
            """,
            connection);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("entity_id", requestId.ToString());
        command.Parameters.AddWithValue("code", code);
        command.Parameters.AddWithValue("message", message);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<RdsMappedWorkSite?> FindMappedWorkSiteAsync(Guid slotId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT map.layout_slot_id, rw.id, rw.rds_site_id, rw.rds_type,
                   rw.last_disabled, rw.last_sync_failed, rw.last_sync_at
            FROM integration.layout_slot_rds_mapping map
            JOIN integration.rds_worksites rw ON rw.id = map.rds_worksite_id
            WHERE map.layout_slot_id = @slot_id
            """,
            connection);
        command.Parameters.AddWithValue("slot_id", slotId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(slotId, reader.GetGuid(1), reader.GetString(2), Number(reader, 3), Number(reader, 4), Number(reader, 5), Timestamp(reader, 6))
            : null;
    }

    public async Task<IReadOnlyList<RdsTaskTemplateSummary>> ListTaskTemplatesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT t.id, t.variant_id, v.code, t.product_group_id, p.part_code,
                   tt.code, t.rds_task_label, t.priority, t.config_json::text, t.is_active
            FROM master.task_templates t
            JOIN master.variants v ON v.id = t.variant_id
            JOIN master.task_types tt ON tt.id = t.task_type_id
            LEFT JOIN master.product_groups pg ON pg.id = t.product_group_id
            LEFT JOIN master.parts p ON p.id = pg.main_part_id
            ORDER BY v.code, p.part_code NULLS FIRST, tt.code, t.id
            """,
            connection);
        var rows = new List<RdsTaskTemplateSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                Uuid(reader, 3), Text(reader, 4), reader.GetString(5), Text(reader, 6),
                reader.GetInt32(7), Json(reader.GetString(8)), reader.GetBoolean(9)));
        }
        return rows;
    }

    public async Task EnrichRemainingTimesAsync(
        IReadOnlyDictionary<string, double> agvRemainingTimes,
        CancellationToken cancellationToken = default)
    {
        if (agvRemainingTimes.Count == 0) return;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        foreach (var (agvId, remainingTime) in agvRemainingTimes)
        {
            await using var command = new NpgsqlCommand(
                """
                UPDATE integration.rds_tasks
                SET last_status_json = COALESCE(last_status_json, '{}'::jsonb)
                        || jsonb_build_object('remaining_time', @remaining_time),
                    updated_at = now()
                WHERE agv_id = @agv_id
                  AND rds_status NOT IN (1001, 1003, 1004, 1005, 1006)
                """,
                connection);
            command.Parameters.AddWithValue("agv_id", agvId);
            command.Parameters.AddWithValue("remaining_time", remainingTime);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<RdsTaskProjection?> FindTaskAsync(string predicate, object value, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(TaskProjectionSelect + $" WHERE {predicate}", connection);
        command.Parameters.AddWithValue("value", value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTaskProjection(reader) : null;
    }

    private async Task UpdateJsonAsync(string sql, Guid requestId, JsonElement json, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("request_id", requestId);
        AddJson(command, "json", json);
        command.Parameters.AddWithValue("at", at);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new RdsExecutionException("RDS_TASK_NOT_FOUND", "The RDS task projection does not exist.");
    }

    private static TransportRequestSnapshot ReadRequest(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetInt64(1), reader.GetString(2),
        WorkflowCode.ParseTransportStatus(reader.GetString(3)),
        WorkflowCode.ParseTaskType(reader.GetString(4)),
        WorkflowCode.ParseRequestOrigin(reader.GetString(5)),
        Uuid(reader, 6), reader.GetGuid(7), reader.GetGuid(8), reader.GetGuid(9),
        Uuid(reader, 10), Uuid(reader, 11), Uuid(reader, 12), Uuid(reader, 13),
        Uuid(reader, 14), Uuid(reader, 15), reader.GetInt32(16),
        WorkflowCode.ParseReadinessState(reader.GetString(17)), Timestamp(reader, 18));

    private static RdsDispatchEndpoint Endpoint(NpgsqlDataReader reader, int start) => new(
        Uuid(reader, start), Text(reader, start + 1), Text(reader, start + 2), Text(reader, start + 3),
        Text(reader, start + 4), Number(reader, start + 5), Number(reader, start + 6),
        Number(reader, start + 7), Number(reader, start + 8), Number(reader, start + 9),
        Timestamp(reader, start + 10), !reader.IsDBNull(start + 4));

    private static RdsTaskProjection ReadTaskProjection(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetString(1), Text(reader, 2), Number(reader, 3), Text(reader, 4),
        Text(reader, 5), Text(reader, 6), Json(reader.GetString(7)), Json(reader.GetString(8)),
        Json(reader.GetString(9)), reader.GetBoolean(10), WorkflowCode.ParseTransportStatus(reader.GetString(11)));

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
    private static Guid? Uuid(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    private static string? Text(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static int? Number(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    private static DateTimeOffset? Timestamp(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private static void AddJson(NpgsqlCommand command, string name, JsonElement value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb) { Value = value.GetRawText() });

    private static void AddNullableText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);

    private sealed class PostgresRdsExecutionLease(NpgsqlConnection connection, Guid requestId) : IRdsExecutionLease
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand(
                    "SELECT pg_advisory_unlock(hashtextextended(@key, 0))",
                    connection);
                command.Parameters.AddWithValue("key", $"tkg:rds:{requestId:D}");
                await command.ExecuteScalarAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
