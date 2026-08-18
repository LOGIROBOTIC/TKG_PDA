using Xunit;

namespace TkgPda.Infrastructure.Postgres.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TKG_PDA_TEST_CONNECTION_STRING")))
        {
            Skip = "Set TKG_PDA_TEST_CONNECTION_STRING or use tests/phase_03/run_postgres_integration.ps1.";
        }
    }
}
