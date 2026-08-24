using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TkgPda.Application;
using TkgPda.Infrastructure.Postgres;
using Xunit;

namespace TkgPda.Api.Tests;

public sealed class LiveRdsPostgresSyncTests
{
    [LiveRdsFact]
    public async Task Actual_read_worksites_sync_idempotently_to_temporary_postgres()
    {
        var baseUrl = Environment.GetEnvironmentVariable("Rds__BaseUrl")!;
        var connectionString = Environment.GetEnvironmentVariable("TKG_PDA_API_TEST_CONNECTION_STRING")!;
        var options = new RdsOptions(true, baseUrl, 10);
        using var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false };
        var client = new RdsReadClient(new LiveFactory(handler, options.BaseAddress!), options, TimeProvider.System, NullLogger<RdsReadClient>.Instance);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var store = new PostgresRdsIntegrationStore(dataSource);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var machineId = await ScalarAsync<Guid>(connectionString, "SELECT id FROM master.machines WHERE code = 'SH-01-A'");
        var navigationId = await ScalarAsync<Guid>(
            connectionString,
            "INSERT INTO master.navigation_profiles (machine_id, code, name, status) VALUES (@machine, @code, 'Live RDS Read Proof', 'DRAFT') RETURNING id",
            ("machine", machineId), ("code", $"P08A-LIVE-{suffix}"));
        try
        {
            var read = await client.GetWorkSitesAsync(Guid.NewGuid());
            Assert.NotEmpty(read.Data);
            var first = await store.SyncWorkSitesAsync(navigationId, read.Data, DateTimeOffset.UtcNow);
            var second = await store.SyncWorkSitesAsync(navigationId, read.Data, DateTimeOffset.UtcNow.AddSeconds(1));

            Assert.Equal(read.Data.Count, first.Created);
            Assert.Equal(0, first.Updated);
            Assert.Equal(0, second.Created);
            Assert.Equal(read.Data.Count, second.Updated);
            Assert.Equal(read.Data.Count, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM integration.rds_worksites WHERE navigation_profile_id = @id", ("id", navigationId)));
            Assert.Equal(read.Data.Count, await ScalarAsync<long>(connectionString, "SELECT count(*) FROM integration.rds_worksites WHERE navigation_profile_id = @id AND raw_state_json ? 'siteId'", ("id", navigationId)));
        }
        finally
        {
            await ExecuteAsync(connectionString, "DELETE FROM master.navigation_profiles WHERE id = @id", ("id", navigationId));
        }
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, parameterValue) in parameters) command.Parameters.AddWithValue(name, parameterValue);
        var result = await command.ExecuteScalarAsync();
        return result is T typed ? typed : (T)Convert.ChangeType(result!, typeof(T));
    }

    private static async Task ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class LiveFactory(HttpMessageHandler handler, Uri baseAddress) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(10) };
    }
}

public sealed class LiveRdsFactAttribute : FactAttribute
{
    public LiveRdsFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("Rds__Enabled") != "true"
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Rds__BaseUrl"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TKG_PDA_API_TEST_CONNECTION_STRING")))
        {
            Skip = "Opt in with Rds__Enabled=true, Rds__BaseUrl, and the temporary PostgreSQL connection string.";
        }
    }
}
