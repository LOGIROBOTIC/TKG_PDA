using System.Text.Json;
using TkgPda.Application;
using Xunit;

namespace TkgPda.Application.Tests;

public sealed class RdsCommissioningDiscoveryTests
{
    [Fact]
    public void Canonical_snapshot_and_report_are_deterministic()
    {
        var snapshot = Snapshot();
        var configuration = Configuration();

        var findingsA = RdsCommissioningDiscoveryService.Compare(snapshot, configuration);
        var findingsB = RdsCommissioningDiscoveryService.Compare(snapshot, configuration);
        var jsonA = RdsCommissioningCanonicalJson.Serialize(snapshot);
        var jsonB = RdsCommissioningCanonicalJson.Serialize(snapshot);
        var reportA = RdsCommissioningMarkdown.Render(snapshot, findingsA, "fixed-sha");
        var reportB = RdsCommissioningMarkdown.Render(snapshot, findingsB, "fixed-sha");

        Assert.Equal(jsonA, jsonB);
        Assert.Equal(findingsA, findingsB);
        Assert.Equal(reportA, reportB);
        Assert.EndsWith("\n", jsonA, StringComparison.Ordinal);
        Assert.Contains("### BLOCKER", reportA, StringComparison.Ordinal);
        Assert.Contains("### NOT_EVALUATED", reportA, StringComparison.Ordinal);
    }

    [Fact]
    public void Comparison_keeps_customer_gaps_separate_from_confirmed_rds_blockers()
    {
        var findings = RdsCommissioningDiscoveryService.Compare(Snapshot(), Configuration());

        Assert.Contains(findings, item => item.Classification == RdsCommissioningFindingClass.Gap && item.Code == "TKG_SLOT_RDS_MAPPING_MISSING");
        Assert.Contains(findings, item => item.Classification == RdsCommissioningFindingClass.Blocker && item.Code == "MAPPED_RDS_WORKSITE_ABSENT");
        Assert.Contains(findings, item => item.Classification == RdsCommissioningFindingClass.Warning && item.Code == "MAPPED_POINT_DISABLED");
        Assert.Contains(findings, item => item.Classification == RdsCommissioningFindingClass.Info && item.Code == "RDS_WORKSITE_UNMAPPED");
        Assert.Contains(findings, item => item.Classification == RdsCommissioningFindingClass.NotEvaluated && item.Code == "EXPECTED_ROBOT_MAPPING_UNDEFINED");
        Assert.Contains(findings, item => item.Classification == RdsCommissioningFindingClass.NotEvaluated && item.Code == "BINTASK_CONTRACT_UNDEFINED");
        Assert.Contains(findings, item => item.Classification == RdsCommissioningFindingClass.NotEvaluated && item.Code == "CORE_PARAMETER_ALLOWLIST_EMPTY");
    }

    [Fact]
    public void Explicit_expected_robot_and_bintask_contracts_become_blockers_when_absent()
    {
        var baseConfiguration = Configuration();
        var configuration = baseConfiguration with
        {
            ExpectedRobots = [new("missing-robot", "SH-01-A")],
            RequiredBinTasks = [new("RDS-1", "RequiredTask", "BRING_EMPTY")]
        };

        var findings = RdsCommissioningDiscoveryService.Compare(Snapshot(), configuration);

        Assert.Contains(findings, item => item.Code == "EXPECTED_ROBOT_ABSENT" && item.Classification == RdsCommissioningFindingClass.Blocker);
        Assert.Contains(findings, item => item.Code == "REQUIRED_BINTASK_ABSENT" && item.Classification == RdsCommissioningFindingClass.Blocker);
        Assert.DoesNotContain(findings, item => item.Code == "EXPECTED_ROBOT_MAPPING_UNDEFINED");
        Assert.DoesNotContain(findings, item => item.Code == "BINTASK_CONTRACT_UNDEFINED");
    }

    [Fact]
    public void Machine_regions_are_not_compared_as_transport_slots()
    {
        var configuration = Configuration() with
        {
            Slots = [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SH-01-A", "L1", "MACHINE", "MACHINE", true, null)]
        };

        var findings = RdsCommissioningDiscoveryService.Compare(Snapshot(), configuration);

        Assert.DoesNotContain(findings, item => item.Code == "TKG_SLOT_RDS_MAPPING_MISSING");
    }

    private static RdsCommissioningConfiguration Configuration()
    {
        var template = new RdsTaskTemplateSummary(
            Guid.NewGuid(), Guid.NewGuid(), "V1", null, null, "BRING_EMPTY", "PROD_BRING_EMPTY", 100,
            Json("{\"rdsInputBindings\":[{\"name\":\"fromPoint\"}]}"), true);
        return new(
            [
                new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SH-01-A", "L1", "S1", "PICKUP", true, null),
                new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SH-01-A", "L1", "S2", "DROP", true, "ABSENT"),
                new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SH-01-A", "L1", "S3", "DROP", true, "RDS-1")
            ],
            [template],
            [],
            []);
    }

    private static RdsCommissioningSnapshot Snapshot() => new(
        "rds_commissioning_snapshot_v1",
        DateTimeOffset.Parse("2026-09-02T10:00:00Z"),
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        new("https://rds.example.test:8088", null, ["map-a"], "scene", "model"),
        [new("RobotsStatus", "GET", "/robotsStatus", "SUCCESS", 200, 0, null, null)],
        [new("robot-uuid", "robot-uuid", "sim_01", "g2", ["Simulation"], "map-a", true, 1, false, null, 0.8, false, "AP15", 1, 2, 0, ["AP20"], null, [])],
        [
            new("RDS-1", true, true, "AP-DISABLED", Json("1"), Json("\"KLT\""), Json("\"P1\""), Json("true"), []),
            new("UNMAPPED", true, true, "AP-X", null, null, null, null, [])
        ],
        [],
        new(["AP-DISABLED"], [], [], []),
        [],
        new("NOT_CAPTURED", new Dictionary<string, JsonElement>()),
        [],
        []);

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
}
