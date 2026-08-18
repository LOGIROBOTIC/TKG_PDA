using Npgsql;
using NpgsqlTypes;
using TkgPda.Application;
using TkgPda.Domain;

namespace TkgPda.Infrastructure.Postgres;

public sealed class PostgresApiDataStore(NpgsqlDataSource dataSource) : IApiDataStore
{
    public async Task<AuthUserRecord?> FindUserByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT u.id,
                   u.username,
                   u.password_hash,
                   u.full_name,
                   u.employee_no,
                   u.is_active,
                   COALESCE(
                       array_agg(r.code ORDER BY r.code) FILTER (WHERE r.id IS NOT NULL),
                       ARRAY[]::varchar[])
            FROM auth.users u
            LEFT JOIN auth.user_roles ur ON ur.user_id = u.id
            LEFT JOIN auth.roles r ON r.id = ur.role_id AND r.is_active
            WHERE u.username = @username
            GROUP BY u.id
            """);
        command.Parameters.AddWithValue("username", username);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AuthUserRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetBoolean(5),
            reader.GetFieldValue<string[]>(6));
    }

    public async Task<DeviceRecord?> FindDeviceAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT id, device_code, name, device_type, is_active FROM audit.client_devices WHERE device_code = @device_code");
        command.Parameters.AddWithValue("device_code", deviceCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new DeviceRecord(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4))
            : null;
    }

    public async Task MarkLoginSucceededAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            "UPDATE auth.users SET last_login_at = now(), updated_at = now() WHERE id = @user_id AND is_active");
        command.Parameters.AddWithValue("user_id", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<MachinePermissionRecord?> FindMachinePermissionAsync(
        Guid userId,
        Guid machineId,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT machine_id, can_view, can_operate, can_change_variant, can_manage_slots
            FROM auth.user_machine_permissions
            WHERE user_id = @user_id AND machine_id = @machine_id
            """);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("machine_id", machineId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadPermission(reader)
            : null;
    }

    public async Task<IReadOnlyList<MachineSummaryRecord>> ListVisibleMachinesAsync(
        Guid userId,
        bool administrator,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT m.id,
                   m.code,
                   m.display_name,
                   m.display_order,
                   CASE WHEN @administrator THEN true ELSE COALESCE(p.can_view, false) END,
                   CASE WHEN @administrator THEN true ELSE COALESCE(p.can_operate, false) END,
                   CASE WHEN @administrator THEN true ELSE COALESCE(p.can_change_variant, false) END,
                   CASE WHEN @administrator THEN true ELSE COALESCE(p.can_manage_slots, false) END
            FROM master.machines m
            LEFT JOIN auth.user_machine_permissions p
              ON p.machine_id = m.id AND p.user_id = @user_id
            WHERE m.is_active
              AND (@administrator OR COALESCE(p.can_view, false))
            ORDER BY m.display_order, m.code
            """);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("administrator", administrator);
        var result = new List<MachineSummaryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MachineSummaryRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                new MachinePermissionRecord(
                    reader.GetGuid(0),
                    reader.GetBoolean(4),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6),
                    reader.GetBoolean(7))));
        }

        return result;
    }

    public async Task<OperationalContextRecord?> GetOperationalContextAsync(
        Guid userId,
        Guid machineId,
        bool administrator,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        MachineSummaryRecord machine;
        Guid? activeVariantId;
        string? activeVariantCode;
        string? activeVariantName;
        Guid? activeLayoutId;
        string? activeLayoutCode;
        string? activeLayoutName;
        Guid? activeNavigationId;
        string? activeNavigationCode;
        long runtimeRowVersion;
        DateTimeOffset? runtimeUpdatedAt;

        await using (var command = new NpgsqlCommand(
                         """
                         SELECT m.id, m.code, m.display_name, m.display_order,
                                CASE WHEN @administrator THEN true ELSE COALESCE(p.can_view, false) END,
                                CASE WHEN @administrator THEN true ELSE COALESCE(p.can_operate, false) END,
                                CASE WHEN @administrator THEN true ELSE COALESCE(p.can_change_variant, false) END,
                                CASE WHEN @administrator THEN true ELSE COALESCE(p.can_manage_slots, false) END,
                                rs.active_variant_id, v.code, v.name,
                                rs.active_layout_id, l.code, l.name,
                                rs.active_navigation_id, n.code,
                                COALESCE(rs.row_version, 0), rs.updated_at
                         FROM master.machines m
                         LEFT JOIN auth.user_machine_permissions p
                           ON p.machine_id = m.id AND p.user_id = @user_id
                         LEFT JOIN ops.machine_runtime_state rs ON rs.machine_id = m.id
                         LEFT JOIN master.variants v ON v.id = rs.active_variant_id
                         LEFT JOIN master.layouts l ON l.id = rs.active_layout_id
                         LEFT JOIN master.navigation_profiles n ON n.id = rs.active_navigation_id
                         WHERE m.id = @machine_id AND m.is_active
                         """,
                         connection))
        {
            command.Parameters.AddWithValue("user_id", userId);
            command.Parameters.AddWithValue("administrator", administrator);
            command.Parameters.AddWithValue("machine_id", machineId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            machine = new MachineSummaryRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                new MachinePermissionRecord(
                    reader.GetGuid(0),
                    reader.GetBoolean(4),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6),
                    reader.GetBoolean(7)));
            activeVariantId = NullableGuid(reader, 8);
            activeVariantCode = NullableString(reader, 9);
            activeVariantName = NullableString(reader, 10);
            activeLayoutId = NullableGuid(reader, 11);
            activeLayoutCode = NullableString(reader, 12);
            activeLayoutName = NullableString(reader, 13);
            activeNavigationId = NullableGuid(reader, 14);
            activeNavigationCode = NullableString(reader, 15);
            runtimeRowVersion = reader.GetInt64(16);
            runtimeUpdatedAt = reader.IsDBNull(17) ? null : reader.GetFieldValue<DateTimeOffset>(17);
        }

        var variants = new List<VariantLayoutOptionRecord>();
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT v.id, v.code, v.name,
                                l.id, l.code, l.name, a.is_default
                         FROM master.variants v
                         JOIN master.variant_layout_assignments a
                           ON a.variant_id = v.id AND a.machine_id = v.machine_id
                         JOIN master.layouts l
                           ON l.id = a.layout_id AND l.machine_id = a.machine_id
                         WHERE v.machine_id = @machine_id
                           AND v.is_active
                           AND l.status = 'PUBLISHED'
                         ORDER BY v.display_order, v.code, a.is_default DESC, l.code
                         """,
                         connection))
        {
            command.Parameters.AddWithValue("machine_id", machineId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                variants.Add(new VariantLayoutOptionRecord(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetGuid(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetBoolean(6)));
            }
        }

        var slots = new List<SlotContextRecord>();
        if (activeLayoutId.HasValue)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT s.id, s.code, s.display_name, s.slot_role, s.is_enabled,
                       s.default_container_type_id, dct.code,
                       s.ui_x, s.ui_y, s.ui_width, s.ui_height, s.display_order,
                       COALESCE(rs.occupancy_state, 'UNKNOWN'),
                       COALESCE(rs.load_state, 'UNKNOWN'),
                       rs.current_part_id, p.part_code,
                       rs.current_container_type_id, cct.code,
                       COALESCE(rs.row_version, 0), rs.updated_at
                FROM master.layout_slots s
                LEFT JOIN master.container_types dct ON dct.id = s.default_container_type_id
                LEFT JOIN ops.slot_runtime_state rs ON rs.layout_slot_id = s.id
                LEFT JOIN master.parts p ON p.id = rs.current_part_id
                LEFT JOIN master.container_types cct ON cct.id = rs.current_container_type_id
                WHERE s.layout_id = @layout_id
                ORDER BY s.display_order, s.code
                """,
                connection);
            command.Parameters.AddWithValue("layout_id", activeLayoutId.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var role = reader.GetString(3);
                var enabled = reader.GetBoolean(4);
                slots.Add(new SlotContextRecord(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    role,
                    enabled,
                    enabled && role != "MACHINE",
                    NullableGuid(reader, 5),
                    NullableString(reader, 6),
                    NullableDecimal(reader, 7),
                    NullableDecimal(reader, 8),
                    NullableDecimal(reader, 9),
                    NullableDecimal(reader, 10),
                    reader.GetInt32(11),
                    reader.GetString(12),
                    reader.GetString(13),
                    NullableGuid(reader, 14),
                    NullableString(reader, 15),
                    NullableGuid(reader, 16),
                    NullableString(reader, 17),
                    reader.GetInt64(18),
                    reader.IsDBNull(19) ? null : reader.GetFieldValue<DateTimeOffset>(19)));
            }
        }

        return new OperationalContextRecord(
            machine,
            activeVariantId,
            activeVariantCode,
            activeVariantName,
            activeLayoutId,
            activeLayoutCode,
            activeLayoutName,
            activeNavigationId,
            activeNavigationCode,
            runtimeRowVersion,
            runtimeUpdatedAt,
            variants,
            slots);
    }

    public async Task<TransportRequestSnapshot?> FindTransportRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT r.id, r.request_no, r.idempotency_key, r.status, tt.code,
                   r.request_origin, r.requested_by, r.machine_id, r.variant_id,
                   r.layout_id, r.navigation_id, r.product_group_id, r.part_id,
                   r.container_type_id, r.source_slot_id, r.destination_slot_id,
                   r.priority
            FROM ops.transport_requests r
            JOIN master.task_types tt ON tt.id = r.task_type_id
            WHERE r.id = @request_id
            """);
        command.Parameters.AddWithValue("request_id", requestId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new TransportRequestSnapshot(
                reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetString(2),
                WorkflowCode.ParseTransportStatus(reader.GetString(3)),
                WorkflowCode.ParseTaskType(reader.GetString(4)),
                WorkflowCode.ParseRequestOrigin(reader.GetString(5)),
                NullableGuid(reader, 6),
                reader.GetGuid(7),
                reader.GetGuid(8),
                reader.GetGuid(9),
                NullableGuid(reader, 10),
                NullableGuid(reader, 11),
                NullableGuid(reader, 12),
                NullableGuid(reader, 13),
                NullableGuid(reader, 14),
                NullableGuid(reader, 15),
                reader.GetInt32(16))
            : null;
    }

    public async Task WriteAuditAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO audit.audit_logs (
                actor_user_id, device_id, action, entity_type, entity_id,
                before_json, after_json, ip_address, user_agent, correlation_id)
            VALUES (
                @actor_user_id, @device_id, @action, @entity_type, @entity_id,
                @before_json, @after_json, CAST(@ip_address AS inet), @user_agent, @correlation_id)
            """);
        AddNullableUuid(command, "actor_user_id", entry.ActorUserId);
        AddNullableUuid(command, "device_id", entry.DeviceId);
        command.Parameters.AddWithValue("action", entry.Action);
        command.Parameters.AddWithValue("entity_type", entry.EntityType);
        AddNullableText(command, "entity_id", entry.EntityId);
        AddNullableJson(command, "before_json", entry.BeforeJson);
        AddNullableJson(command, "after_json", entry.AfterJson);
        AddNullableText(command, "ip_address", entry.IpAddress);
        AddNullableText(command, "user_agent", entry.UserAgent);
        command.Parameters.AddWithValue("correlation_id", entry.CorrelationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            return await command.ExecuteScalarAsync(cancellationToken) is 1;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }

    private static MachinePermissionRecord ReadPermission(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetBoolean(1),
        reader.GetBoolean(2),
        reader.GetBoolean(3),
        reader.GetBoolean(4));

    private static Guid? NullableGuid(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    private static string? NullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static decimal? NullableDecimal(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Uuid).Value = value.HasValue ? value.Value : DBNull.Value;

    private static void AddNullableText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Text).Value = value is null ? DBNull.Value : value;

    private static void AddNullableJson(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Jsonb).Value = value is null ? DBNull.Value : value;
}
