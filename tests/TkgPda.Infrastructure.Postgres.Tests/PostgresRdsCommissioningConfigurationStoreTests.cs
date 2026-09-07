using Npgsql;
using Xunit;

namespace TkgPda.Infrastructure.Postgres.Tests;

public sealed class PostgresRdsCommissioningConfigurationStoreTests
{
    private readonly string connectionString = Environment.GetEnvironmentVariable("TKG_PDA_TEST_CONNECTION_STRING") ?? string.Empty;

    [PostgresFact]
    public async Task Baseline_configuration_is_read_without_inventing_robot_or_bintask_contracts()
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var configuration = await new PostgresRdsCommissioningConfigurationStore(dataSource).LoadAsync();

        var baselineSlots = configuration.Slots.Where(s =>
            !s.SlotCode.StartsWith("EMPTYBUF-")
            && !s.SlotCode.StartsWith("TEST-")
            && !s.SlotCode.StartsWith("SLOTPART-")
            && !s.SlotCode.StartsWith("BUFFER-TEST-")).ToList();
        Assert.Equal(183, baselineSlots.Count);
        Assert.Equal(11, baselineSlots.Count(item => item.SlotRole == "MACHINE"));
        Assert.Empty(configuration.ExpectedRobots);
        Assert.Empty(configuration.RequiredBinTasks);
        Assert.All(baselineSlots, slot => Assert.NotEmpty(slot.MachineCode));
    }
}
