using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using TkgPda.Api;

namespace TkgPda.Api.Tests;

public sealed class ApiTestFixture : WebApplicationFactory<Program>
{
    public string ConnectionString => Environment.GetEnvironmentVariable("TKG_PDA_API_TEST_CONNECTION_STRING")
        ?? throw new InvalidOperationException("TKG_PDA_API_TEST_CONNECTION_STRING is required.");

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async Task ResetAsync()
    {
        await ExecuteAsync(
            """
            TRUNCATE
                integration.rds_tasks,
                ops.transport_request_events,
                ops.transport_requests,
                ops.slot_runtime_state,
                ops.machine_runtime_state
            RESTART IDENTITY CASCADE;
            DELETE FROM audit.audit_logs;
            DELETE FROM auth.user_machine_permissions;
            DELETE FROM auth.user_roles;
            DELETE FROM auth.users;
            DELETE FROM audit.client_devices;
            """);
    }

    public async Task<TestIdentity> SeedIdentityAsync(
        string role = RoleCodes.LineOperator,
        bool active = true,
        bool deviceActive = true,
        bool canView = true,
        bool canOperate = true,
        bool canChangeVariant = false,
        string? machineCode = "SH-01-A")
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var username = $"user-{suffix}";
        var password = "Test-password-42!";
        var deviceCode = $"PDA-{suffix}";
        var hasher = new Pbkdf2PasswordHasher();
        var userId = await ScalarAsync<Guid>(
            """
            INSERT INTO auth.users (username, password_hash, full_name, employee_no, is_active)
            VALUES (@username, @password_hash, @full_name, @employee_no, @active)
            RETURNING id
            """,
            ("username", username),
            ("password_hash", hasher.Hash(password)),
            ("full_name", $"Test User {suffix}"),
            ("employee_no", $"E-{suffix}"),
            ("active", active));
        await ExecuteAsync(
            """
            INSERT INTO auth.user_roles (user_id, role_id)
            SELECT @user_id, id FROM auth.roles WHERE code = @role
            """,
            ("user_id", userId),
            ("role", role));
        var deviceId = await ScalarAsync<Guid>(
            """
            INSERT INTO audit.client_devices (device_code, name, device_type, is_active)
            VALUES (@code, @name, 'PDA', @active)
            RETURNING id
            """,
            ("code", deviceCode),
            ("name", $"Test PDA {suffix}"),
            ("active", deviceActive));

        Guid? machineId = null;
        if (machineCode is not null)
        {
            machineId = await ScalarAsync<Guid>("SELECT id FROM master.machines WHERE code = @code", ("code", machineCode));
            await ExecuteAsync(
                """
                INSERT INTO auth.user_machine_permissions (
                    user_id, machine_id, can_view, can_operate, can_change_variant, can_manage_slots)
                VALUES (@user_id, @machine_id, @can_view, @can_operate, @can_change_variant, false)
                """,
                ("user_id", userId),
                ("machine_id", machineId),
                ("can_view", canView),
                ("can_operate", canOperate),
                ("can_change_variant", canChangeVariant));
        }

        return new TestIdentity(userId, username, password, deviceId, deviceCode, machineId);
    }

    public async Task<HttpClient> LoginClientAsync(TestIdentity identity)
    {
        var client = Client();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(identity.Username, identity.Password))
        };
        request.Headers.Add(ApiHeaders.DeviceCode, identity.DeviceCode);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("Login response was empty.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        client.DefaultRequestHeaders.Add(ApiHeaders.DeviceCode, identity.DeviceCode);
        return client;
    }

    public async Task<LayoutContext> ConfigureMachineAsync(
        string machineCode = "SH-01-A",
        string variantCode = "1")
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

    public async Task<LayoutContext> ContextAsync(string machineCode, string variantCode)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT m.id, v.id, a.layout_id
            FROM master.machines m
            JOIN master.variants v ON v.machine_id = m.id AND v.code = @variant_code
            JOIN master.variant_layout_assignments a ON a.variant_id = v.id AND a.machine_id = m.id
            JOIN master.layouts l ON l.id = a.layout_id AND l.status = 'PUBLISHED'
            WHERE m.code = @machine_code
            ORDER BY a.is_default DESC, l.code
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("machine_code", machineCode);
        command.Parameters.AddWithValue("variant_code", variantCode);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"Context not found for {machineCode}/{variantCode}.");
        }

        return new LayoutContext(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2));
    }

    public Task<Guid[]> LogisticsSlotsAsync(Guid layoutId, int count) => QueryGuidsAsync(
        """
        SELECT id FROM master.layout_slots
        WHERE layout_id = @layout_id AND slot_role <> 'MACHINE' AND is_enabled
        ORDER BY display_order, code
        LIMIT @count
        """,
        ("layout_id", layoutId),
        ("count", count));

    public Task SetSlotAsync(Guid slotId, string occupancy, string load) => ExecuteAsync(
        """
        INSERT INTO ops.slot_runtime_state (layout_slot_id, occupancy_state, load_state)
        VALUES (@slot_id, @occupancy, @load)
        ON CONFLICT (layout_slot_id) DO UPDATE
        SET occupancy_state = EXCLUDED.occupancy_state,
            load_state = EXCLUDED.load_state,
            current_part_id = NULL,
            current_container_type_id = NULL,
            row_version = ops.slot_runtime_state.row_version + 1,
            updated_at = now()
        """,
        ("slot_id", slotId),
        ("occupancy", occupancy),
        ("load", load));

    public async Task<HttpResponseMessage> SendTransportAsync(
        HttpClient client,
        CreateTransportHttpRequest body,
        string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transport-requests")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add(ApiHeaders.IdempotencyKey, idempotencyKey);
        return await client.SendAsync(request);
    }

    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        AddParameters(command, parameters);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        AddParameters(command, parameters);
        var value = await command.ExecuteScalarAsync();
        return value is T typed ? typed : throw new InvalidOperationException($"Scalar query did not return {typeof(T).Name}.");
    }

    private async Task<Guid[]> QueryGuidsAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        AddParameters(command, parameters);
        var values = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetGuid(0));
        }

        return values.ToArray();
    }

    private static void AddParameters(NpgsqlCommand command, IEnumerable<(string Name, object? Value)> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }
}

public sealed record TestIdentity(
    Guid UserId,
    string Username,
    string Password,
    Guid DeviceId,
    string DeviceCode,
    Guid? MachineId);

public sealed record LayoutContext(Guid MachineId, Guid VariantId, Guid LayoutId);
