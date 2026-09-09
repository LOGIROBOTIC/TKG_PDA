using Microsoft.Extensions.Configuration;
using TkgPda.Api;
using TkgPda.Application;
using Xunit;

namespace TkgPda.Api.Tests;

/// <summary>
/// The two RDS switches are configuration, not application modes. TKG has exactly one
/// execution path: whether RDS is currently driving its own simulation or physical
/// robots is RDS's business and is invisible here — there is no simulation flag to
/// set, and no branch that could take a different route for one or the other.
///
/// A live commissioning test lost time to this distinction. RDSCore was connected,
/// the robot was online and dispatchable, the request reached QUEUED, and nothing
/// moved, because the running API had Rds:DispatchEnabled=false. That is the gate
/// doing its job, not an engine defect, so these tests pin what each switch means:
///
///   Rds:Enabled          — the integration is active at all (reads/telemetry)
///   Rds:DispatchEnabled  — TKG may currently send execution mutations
/// </summary>
public sealed class RdsDispatchConfigurationTests
{
    private static RdsOptions FromSettings(params (string Key, string Value)[] settings) =>
        RdsOptions.FromConfiguration(
            new ConfigurationBuilder()
                .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
                .Build());

    // The committed default. It must stay closed so that a host which supplies
    // nothing can never move a robot.
    [Fact]
    public void The_shipped_default_keeps_dispatch_closed()
    {
        var options = RdsOptions.FromConfiguration(
            new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false)
                .Build());

        Assert.True(options.Enabled, "RDS integration is on by default so telemetry works.");
        Assert.False(options.DispatchEnabled, "Physical dispatch must be opt-in per runtime.");
    }

    // A — integration off: nothing RDS-related may execute.
    [Fact]
    public void Rds_disabled_permits_no_execution_at_all()
    {
        var options = FromSettings(("Rds:Enabled", "false"), ("Rds:BaseUrl", "http://127.0.0.1:8080"));
        options.Validate();

        Assert.False(options.Enabled);
        Assert.False(options.DispatchEnabled);
        Assert.False(RdsMutationPolicy.IsAllowed(RdsMutationClass.ExecutionMutation, options.DispatchEnabled));
        Assert.False(RdsMutationPolicy.IsAllowed(RdsMutationClass.MotionEnabling, options.DispatchEnabled));
    }

    // Enabling dispatch without enabling the integration is a contradiction and is
    // refused at startup rather than silently half-working.
    [Fact]
    public void Dispatch_without_integration_is_rejected_at_startup()
    {
        // FromConfiguration validates as it binds, so the host fails to start rather
        // than coming up with a contradictory RDS configuration.
        var error = Assert.Throws<InvalidOperationException>(
            () => FromSettings(("Rds:Enabled", "false"), ("Rds:DispatchEnabled", "true")));

        Assert.Contains("Rds:DispatchEnabled requires Rds:Enabled", error.Message, StringComparison.Ordinal);
    }

    // B — the exact state the live test was in: reads fine, execution refused.
    [Fact]
    public void Enabled_with_dispatch_closed_allows_reads_but_refuses_execution()
    {
        var options = FromSettings(("Rds:Enabled", "true"), ("Rds:BaseUrl", "http://127.0.0.1:8080"));
        options.Validate();

        Assert.True(options.Enabled, "Telemetry and WorkSite reads stay available.");
        Assert.False(options.DispatchEnabled);

        Assert.True(RdsMutationPolicy.IsAllowed(RdsMutationClass.ReadOnly, options.DispatchEnabled));
        // Safety-reducing actions stay reachable; that is deliberate, not a leak.
        Assert.True(RdsMutationPolicy.IsAllowed(RdsMutationClass.SafetyStop, options.DispatchEnabled));
        Assert.False(RdsMutationPolicy.IsAllowed(RdsMutationClass.MotionEnabling, options.DispatchEnabled));
        Assert.False(RdsMutationPolicy.IsAllowed(RdsMutationClass.ExecutionMutation, options.DispatchEnabled));
    }

    // C — the commissioning runtime: the same single path, now permitted.
    [Fact]
    public void Enabled_with_dispatch_open_permits_the_normal_execution_path()
    {
        var options = FromSettings(
            ("Rds:Enabled", "true"),
            ("Rds:DispatchEnabled", "true"),
            ("Rds:BaseUrl", "http://127.0.0.1:8080"),
            ("Rds:IntegrationKey", new string('i', 40)),
            ("Rds:CallbackUrl", "http://127.0.0.1/callback"),
            ("Rds:CallbackKey", new string('c', 40)));
        options.Validate();

        Assert.True(options.DispatchEnabled);
        Assert.True(RdsMutationPolicy.IsAllowed(RdsMutationClass.MotionEnabling, options.DispatchEnabled));
        Assert.True(RdsMutationPolicy.IsAllowed(RdsMutationClass.ExecutionMutation, options.DispatchEnabled));
    }

    // The override is an environment concern. This mirrors what the Commissioning
    // launch profile and a Rds__DispatchEnabled=true environment variable do, and
    // proves no source edit is needed to open or close the gate.
    [Fact]
    public void The_runtime_environment_alone_can_open_the_gate()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("Rds:DispatchEnabled", "true"),
                new KeyValuePair<string, string?>("Rds:IntegrationKey", new string('i', 40)),
                new KeyValuePair<string, string?>("Rds:CallbackUrl", "http://127.0.0.1/callback"),
                new KeyValuePair<string, string?>("Rds:CallbackKey", new string('c', 40)),
            ])
            .Build();

        var options = RdsOptions.FromConfiguration(configuration);
        options.Validate();

        Assert.True(options.DispatchEnabled);
    }

    // D — there is no simulation switch to find. If one is ever introduced, this
    // fails and the architecture decision gets re-litigated deliberately.
    [Theory]
    [InlineData("Rds:SimulationMode")]
    [InlineData("Rds:IsSimulation")]
    [InlineData("Rds:SimulationDispatchEnabled")]
    [InlineData("RdsCoreExecution:SimulationMode")]
    public void No_simulation_switch_participates_in_dispatch_permission(string simulationKey)
    {
        var options = FromSettings(
            ("Rds:Enabled", "true"),
            ("Rds:BaseUrl", "http://127.0.0.1:8080"),
            (simulationKey, "true"));
        options.Validate();

        // Setting a simulation-looking key changes nothing: the gate is still shut,
        // because dispatch permission comes only from Rds:DispatchEnabled.
        Assert.False(options.DispatchEnabled);
        Assert.False(RdsMutationPolicy.IsAllowed(RdsMutationClass.ExecutionMutation, options.DispatchEnabled));
    }

    // E — the policy, not the caller, decides. Same table for every engine and role.
    [Theory]
    [InlineData(RdsMutationClass.ReadOnly, false, true)]
    [InlineData(RdsMutationClass.ReadOnly, true, true)]
    [InlineData(RdsMutationClass.SafetyStop, false, true)]
    [InlineData(RdsMutationClass.SafetyStop, true, true)]
    [InlineData(RdsMutationClass.MotionEnabling, false, false)]
    [InlineData(RdsMutationClass.MotionEnabling, true, true)]
    [InlineData(RdsMutationClass.ExecutionMutation, false, false)]
    [InlineData(RdsMutationClass.ExecutionMutation, true, true)]
    public void RdsMutationPolicy_is_the_single_authority(
        RdsMutationClass mutationClass,
        bool dispatchEnabled,
        bool expected)
    {
        Assert.Equal(expected, RdsMutationPolicy.IsAllowed(mutationClass, dispatchEnabled));
    }

    // F — making a robot undispatchable reduces motion and stays available with the
    // gate shut; making it dispatchable again does not.
    [Theory]
    [InlineData("dispatchable", RdsMutationClass.MotionEnabling)]
    [InlineData("undispatchable", RdsMutationClass.SafetyStop)]
    [InlineData("", RdsMutationClass.SafetyStop)]
    public void Dispatchable_classification_follows_the_direction_of_motion(string type, RdsMutationClass expected)
    {
        Assert.Equal(expected, RdsMutationPolicy.ClassifyDispatchable(type));
    }
}
