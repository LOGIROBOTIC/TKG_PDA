using System.Text.Json;
using Npgsql;
using TkgPda.Application;
using Xunit;

namespace TkgPda.Infrastructure.Postgres.Tests;

public sealed class PostgresRdsReadinessTests
{
    private readonly string connectionString = Environment.GetEnvironmentVariable("TKG_PDA_TEST_CONNECTION_STRING") ?? string.Empty;

    [PostgresFact]
    public async Task Read_fixture_sync_mapping_audit_and_operational_projection_are_safe_and_idempotent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var correlationId = Guid.NewGuid();
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var store = new PostgresRdsIntegrationStore(dataSource);
        var apiStore = new PostgresApiDataStore(dataSource);
        var machineId = await ScalarAsync<Guid>("SELECT id FROM master.machines WHERE code = 'SH-01-A'");
        var layoutId = await ScalarAsync<Guid>("SELECT id FROM master.layouts WHERE machine_id = @machine AND status = 'PUBLISHED' ORDER BY code LIMIT 1", ("machine", machineId));
        var variantId = await ScalarAsync<Guid>("SELECT variant_id FROM master.variant_layout_assignments WHERE layout_id = @layout ORDER BY is_default DESC LIMIT 1", ("layout", layoutId));
        var logisticsSlotId = await ScalarAsync<Guid>("SELECT id FROM master.layout_slots WHERE layout_id = @layout AND slot_role <> 'MACHINE' ORDER BY display_order, code LIMIT 1", ("layout", layoutId));
        var machineSlotId = await ScalarAsync<Guid>("SELECT id FROM master.layout_slots WHERE layout_id = @layout AND slot_role = 'MACHINE' ORDER BY display_order, code LIMIT 1", ("layout", layoutId));
        var userId = await ScalarAsync<Guid>(
            "INSERT INTO auth.users (username, password_hash, full_name) VALUES (@username, 'test-hash', 'Phase 08A Test') RETURNING id",
            ("username", $"p08a-{suffix}"));
        var navigationId = await ScalarAsync<Guid>(
            """
            INSERT INTO master.navigation_profiles (machine_id, code, name, status, created_by)
            VALUES (@machine, @code, 'Phase 08A Fixture', 'DRAFT', @user)
            RETURNING id
            """,
            ("machine", machineId), ("code", $"P08A-{suffix}"), ("user", userId));
        var context = new AdminMutationContext(userId, correlationId, "127.0.0.1", "phase-08a-test");

        try
        {
            var first = new[]
            {
                Site($"P08A-PHYSICAL-{suffix}", 1, filled: 0, locked: 0, disabled: 0, syncFailed: 0, revision: 1),
                Site($"P08A-LOGICAL-{suffix}", 0, filled: 0, locked: 0, disabled: 0, syncFailed: 0, revision: 1),
                Site($"P08A-SYNCFAIL-{suffix}", 1, filled: 1, locked: 0, disabled: 0, syncFailed: 1, revision: 1)
            };
            var firstCounts = await store.SyncWorkSitesAsync(navigationId, first, DateTimeOffset.UtcNow);
            Assert.Equal((3, 0), firstCounts);

            var second = new[]
            {
                Site($"P08A-PHYSICAL-{suffix}", 1, filled: 1, locked: 0, disabled: 1, syncFailed: 0, revision: 2),
                first[1],
                first[2]
            };
            var secondCounts = await store.SyncWorkSitesAsync(navigationId, second, DateTimeOffset.UtcNow.AddSeconds(1));
            Assert.Equal((0, 3), secondCounts);
            Assert.Equal(3, await ScalarAsync<long>("SELECT count(*) FROM integration.rds_worksites WHERE navigation_profile_id = @id", ("id", navigationId)));
            Assert.Equal("2", await ScalarAsync<string>("SELECT raw_state_json ->> 'revision' FROM integration.rds_worksites WHERE navigation_profile_id = @id AND rds_site_id LIKE 'P08A-PHYSICAL-%'", ("id", navigationId)));

            var mirrored = await store.ListWorkSitesAsync(new RdsWorkSiteFilter(navigationId));
            var disabledPhysical = mirrored.Single(site => site.SiteId.StartsWith("P08A-PHYSICAL-", StringComparison.Ordinal));
            var logical = mirrored.Single(site => site.Type == 0);
            var syncFailed = mirrored.Single(site => site.SyncFailed == 1);
            Assert.False(disabledPhysical.ExecutionReady);
            Assert.False(syncFailed.ExecutionReady);

            var mappedDisabled = await store.SetSlotMappingAsync(logisticsSlotId, disabledPhysical.Id, context);
            Assert.Equal(disabledPhysical.Id, mappedDisabled.RdsWorkSiteId);
            Assert.False(mappedDisabled.ExecutionReady);

            var machineError = await Assert.ThrowsAsync<RdsIntegrationRuleException>(() => store.SetSlotMappingAsync(machineSlotId, disabledPhysical.Id, context));
            Assert.Equal("MACHINE_REGION_MAPPING_FORBIDDEN", machineError.Code);
            var logicalError = await Assert.ThrowsAsync<RdsIntegrationRuleException>(() => store.SetSlotMappingAsync(logisticsSlotId, logical.Id, context));
            Assert.Equal("RDS_WORKSITE_NOT_PHYSICAL", logicalError.Code);

            var mappedSyncFailed = await store.SetSlotMappingAsync(logisticsSlotId, syncFailed.Id, context);
            Assert.False(mappedSyncFailed.ExecutionReady);
            await ExecuteAsync(
                """
                INSERT INTO ops.machine_runtime_state (machine_id, active_variant_id, active_layout_id, active_navigation_id)
                VALUES (@machine, @variant, @layout, @navigation)
                ON CONFLICT (machine_id) DO UPDATE
                SET active_variant_id = EXCLUDED.active_variant_id,
                    active_layout_id = EXCLUDED.active_layout_id,
                    active_navigation_id = EXCLUDED.active_navigation_id,
                    row_version = ops.machine_runtime_state.row_version + 1,
                    updated_at = now()
                """,
                ("machine", machineId), ("variant", variantId), ("layout", layoutId), ("navigation", navigationId));
            var data = await apiStore.GetOperationalContextDataAsync(userId, machineId, administrator: true);
            var projected = PdaOperationalContextPolicy.Project(
                Assert.IsType<OperationalContextDataRecord>(data),
                new PdaActorContext(userId, new HashSet<string> { KnownRoleCodes.Admin }, true));
            var slot = projected.Slots.Single(item => item.Id == logisticsSlotId);
            // Sync failure surfaces as a business readiness code, not a raw site id.
            Assert.True(slot.ExecutionReadiness.RdsWorkSiteMapped);
            Assert.False(slot.ExecutionReadiness.IsExecutionReady);
            Assert.Equal("RDS_WORKSITE_SYNC_FAILED", slot.ExecutionReadiness.ReadinessCode);

            await store.RemoveSlotMappingAsync(logisticsSlotId, context);
            Assert.Null((await store.ListSlotMappingsAsync(layoutId)).Single(item => item.SlotId == logisticsSlotId).RdsWorkSiteId);
            Assert.Equal(3, await ScalarAsync<long>("SELECT count(*) FROM audit.audit_logs WHERE correlation_id = @id AND entity_type = 'LAYOUT_SLOT_RDS_MAPPING'", ("id", correlationId)));
        }
        finally
        {
            await ExecuteAsync("DELETE FROM ops.machine_runtime_state WHERE machine_id = @id", ("id", machineId));
            await ExecuteAsync("DELETE FROM audit.audit_logs WHERE correlation_id = @id", ("id", correlationId));
            await ExecuteAsync("DELETE FROM master.navigation_profiles WHERE id = @id", ("id", navigationId));
            await ExecuteAsync("DELETE FROM auth.users WHERE id = @id", ("id", userId));
        }
    }

    private static RdsWorkSiteSnapshot Site(string id, int type, int filled, int locked, int disabled, int syncFailed, int revision)
    {
        using var json = JsonDocument.Parse($$"""{"siteId":"{{id}}","type":{{type}},"filled":{{filled}},"locked":{{locked}},"disabled":{{disabled}},"syncFailed":{{syncFailed}},"content":"","revision":{{revision}}}""");
        return new(id, "AREA-1", "GROUP-1", type, filled, locked, null, disabled, syncFailed, string.Empty, null, json.RootElement.Clone());
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        var result = await command.ExecuteScalarAsync();
        return result is T typed ? typed : (T)Convert.ChangeType(result!, typeof(T));
    }
}
