using Npgsql;
using TkgPda.Application;
using TkgPda.Infrastructure.Postgres;
using Xunit;

namespace TkgPda.Infrastructure.Postgres.Tests;

// WP-01A — auth.users.employee_no normalized-uniqueness invariant enforced by
// db/migrations/002_operator_employee_identity.sql, plus the indexed store lookup.
public sealed class PostgresEmployeeNumberIdentityTests
{
    private readonly string _connectionString = Environment.GetEnvironmentVariable("TKG_PDA_TEST_CONNECTION_STRING") ?? string.Empty;

    [PostgresFact]
    public async Task Migration_creates_the_normalized_employee_number_unique_index()
    {
        await ResetAsync();
        var exists = await ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'auth' AND indexname = 'uq_users_employee_no_normalized')");
        Assert.True(exists);
    }

    [PostgresFact]
    public async Task Employee_number_uniqueness_is_case_insensitive()
    {
        await ResetAsync();
        await InsertUserAsync("emp-test-case-a", "ABC123");
        var error = await Assert.ThrowsAsync<PostgresException>(() => InsertUserAsync("emp-test-case-b", "abc123"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }

    [PostgresFact]
    public async Task Employee_number_uniqueness_ignores_surrounding_whitespace()
    {
        await ResetAsync();
        await InsertUserAsync("emp-test-ws-a", "ABC123");
        var error = await Assert.ThrowsAsync<PostgresException>(() => InsertUserAsync("emp-test-ws-b", " ABC123 "));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }

    [PostgresFact]
    public async Task Leading_zero_employee_numbers_remain_distinct()
    {
        await ResetAsync();
        await InsertUserAsync("emp-test-zero-a", "001245");
        await InsertUserAsync("emp-test-zero-b", "1245");
        Assert.Equal(2, await ScalarAsync<long>(
            "SELECT count(*) FROM auth.users WHERE username LIKE 'emp-test-zero-%'"));
    }

    [PostgresFact]
    public async Task Multiple_null_employee_numbers_are_allowed()
    {
        await ResetAsync();
        await InsertUserAsync("emp-test-null-a", null);
        await InsertUserAsync("emp-test-null-b", null);
        Assert.Equal(2, await ScalarAsync<long>(
            "SELECT count(*) FROM auth.users WHERE username LIKE 'emp-test-null-%'"));
    }

    [PostgresFact]
    public async Task Blank_employee_numbers_are_exempt_from_uniqueness()
    {
        await ResetAsync();
        await InsertUserAsync("emp-test-blank-a", "   ");
        await InsertUserAsync("emp-test-blank-b", "");
        Assert.Equal(2, await ScalarAsync<long>(
            "SELECT count(*) FROM auth.users WHERE username LIKE 'emp-test-blank-%'"));
    }

    [PostgresFact]
    public async Task Store_resolves_user_by_normalized_employee_number()
    {
        await ResetAsync();
        var userId = await InsertUserAsync("emp-test-lookup", "ab001");
        await using var dataSource = NpgsqlDataSource.Create(_connectionString);
        var store = new PostgresApiDataStore(dataSource);

        var match = await store.FindUserByEmployeeNumberAsync(EmployeeNumber.NormalizeForLookup("  AB001 ")!);
        var miss = await store.FindUserByEmployeeNumberAsync(EmployeeNumber.NormalizeForLookup("does-not-exist")!);

        Assert.NotNull(match);
        Assert.Equal(userId, match!.Id);
        Assert.Equal("ab001", match.EmployeeNo);
        Assert.Null(miss);
    }

    private async Task ResetAsync() =>
        await ExecuteAsync("DELETE FROM auth.users WHERE username LIKE 'emp-test-%'");

    private async Task<Guid> InsertUserAsync(string username, object? employeeNo)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO auth.users (username, password_hash, full_name, employee_no)
            VALUES (@username, '$tkg-pbkdf2-sha512$test', 'Employee Test', @employee_no)
            RETURNING id
            """,
            connection);
        command.Parameters.AddWithValue("username", username);
        command.Parameters.AddWithValue("employee_no", employeeNo ?? DBNull.Value);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

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
