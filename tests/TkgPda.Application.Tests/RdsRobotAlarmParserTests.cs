using System.Text.Json;
using TkgPda.Application;
using Xunit;

namespace TkgPda.Application.Tests;

/// <summary>
/// Fixtures here are copied from the shapes in RDS DÖKÜMANLARI/RdsScriptMethods/
/// RdsScriptMethods_en.md rather than invented, because the native telemetry parser
/// previously read "alarms.fatals" and "rbk_report.alarms.fatals" — paths that appear
/// nowhere in the SEER documentation — and its unit test used a matching invented
/// payload, so both agreed with each other and neither agreed with the robot.
///
/// Every assertion checks the extracted code AND the preserved severity. Asserting
/// only "some alarm was found" is what allowed a notice to behave like a fatal.
/// </summary>
public sealed class RdsRobotAlarmParserTests
{
    private static IReadOnlyList<RdsParsedAlarm> ParseRobot(string json) =>
        RdsRobotAlarmParser.ParseRobot(JsonDocument.Parse(json).RootElement);

    private static IReadOnlyList<RdsParsedAlarm> ParseContainer(string json)
    {
        var alarms = new List<RdsParsedAlarm>();
        RdsRobotAlarmParser.Append(alarms, JsonDocument.Parse(json).RootElement, new HashSet<string>(StringComparer.Ordinal));
        return alarms;
    }

    // A — core-level envelope (buckets are siblings of "report") with shape A.
    [Fact]
    public void Core_level_envelope_with_explicit_code_is_parsed_with_severity()
    {
        var alarms = ParseContainer(
            """
            {
              "errors": [
                { "code": 52201, "desc": "The number of robots in same block group is over max number.", "times": 1, "timestamp": 1681110452 }
              ],
              "warnings": [
                { "code": 54000, "desc": "unable to reach park point.", "times": 1, "timestamp": 1681110462 }
              ],
              "fatals": [],
              "notices": []
            }
            """);

        var error = Assert.Single(alarms, a => a.Severity == RdsAlarmSeverity.Error);
        Assert.Equal("52201", error.Code);
        Assert.Equal(1, error.Times);
        Assert.True(error.IsBlocking);

        var warning = Assert.Single(alarms, a => a.Severity == RdsAlarmSeverity.Warning);
        Assert.Equal("54000", warning.Code);
        Assert.False(warning.IsBlocking);
    }

    // B — core-level envelope with shape B (code is the property NAME).
    [Fact]
    public void Core_level_envelope_with_dynamically_keyed_code_is_parsed_with_severity()
    {
        var alarms = ParseContainer(
            """
            {
              "errors": [
                { "52101": 1634020173, "desc": "hu-test-1,hu-test-3, have no charge point in map.", "times": 1 },
                { "52103": 1634020173, "desc": "hu-test-1,hu-test-3, have no park point in map.", "times": 1 }
              ],
              "fatals": [],
              "notices": []
            }
            """);

        Assert.Equal(2, alarms.Count);
        Assert.All(alarms, a => Assert.Equal(RdsAlarmSeverity.Error, a.Severity));
        Assert.Contains(alarms, a => a.Code == "52101" && a.OccurredAt == "1634020173");
        Assert.Contains(alarms, a => a.Code == "52103");
    }

    // C — robot-level envelope (inside rbk_report) with shape A.
    [Fact]
    public void Robot_level_rbk_report_envelope_with_explicit_code_is_parsed()
    {
        var alarms = ParseRobot(
            """
            {
              "vehicle_id": "AMB-01",
              "rbk_report": {
                "current_station": "LM1",
                "errors": [ { "code": 52201, "desc": "blocking fault", "times": 2, "timestamp": 1681110452 } ],
                "fatals": [], "warnings": [], "notices": []
              }
            }
            """);

        var alarm = Assert.Single(alarms);
        Assert.Equal("52201", alarm.Code);
        Assert.Equal(RdsAlarmSeverity.Error, alarm.Severity);
        Assert.Equal(2, alarm.Times);
    }

    // D — robot-level envelope with shape B, exactly as documented at line 1195.
    [Fact]
    public void Robot_level_rbk_report_envelope_with_dynamically_keyed_code_is_parsed()
    {
        var alarms = ParseRobot(
            """
            {
              "uuid": "hu-test-1",
              "vehicle_id": "AMB-01",
              "rbk_report": {
                "warnings": [
                  { "55001": 1634020734, "desc": "The robot is in the dispatching state.", "times": 1 }
                ],
                "errors": [], "fatals": [], "notices": []
              }
            }
            """);

        var alarm = Assert.Single(alarms);
        Assert.Equal("55001", alarm.Code);
        Assert.Equal(RdsAlarmSeverity.Warning, alarm.Severity);
        Assert.Equal("1634020734", alarm.OccurredAt);
        Assert.False(alarm.IsBlocking);
    }

    // E/F/G/H — every severity keeps its own meaning; only fatal/error block.
    [Theory]
    [InlineData("notices", RdsAlarmSeverity.Notice, false)]
    [InlineData("warnings", RdsAlarmSeverity.Warning, false)]
    [InlineData("errors", RdsAlarmSeverity.Error, true)]
    [InlineData("fatals", RdsAlarmSeverity.Fatal, true)]
    public void Each_documented_bucket_maps_to_its_own_severity(string bucket, RdsAlarmSeverity expected, bool blocking)
    {
        var alarms = ParseRobot(
            $$"""
            {
              "vehicle_id": "AMB-01",
              "rbk_report": { "{{bucket}}": [ { "code": 12345, "desc": "d", "times": 1 } ] }
            }
            """);

        var alarm = Assert.Single(alarms);
        Assert.Equal("12345", alarm.Code);
        Assert.Equal(expected, alarm.Severity);
        Assert.Equal(blocking, alarm.IsBlocking);
    }

    // I — a malformed sibling is skipped without discarding the valid alarm or the
    // rest of the robot snapshot.
    [Fact]
    public void Malformed_entries_are_skipped_without_dropping_valid_alarms()
    {
        var alarms = ParseRobot(
            """
            {
              "vehicle_id": "AMB-01",
              "rbk_report": {
                "errors": [
                  "not-an-object",
                  12345,
                  null,
                  { "desc": "no code at all", "times": 1 },
                  { "nested": { "still": "not a code" }, "desc": "unusable value kind" },
                  { "code": 52201, "desc": "the real one", "times": 1 }
                ]
              }
            }
            """);

        var alarm = Assert.Single(alarms);
        Assert.Equal("52201", alarm.Code);
        Assert.Equal(RdsAlarmSeverity.Error, alarm.Severity);
    }

    // J — empty and absent buckets produce no alarms and no exception.
    [Theory]
    [InlineData("""{ "vehicle_id": "AMB-01", "rbk_report": { "errors": [], "fatals": [], "warnings": [], "notices": [] } }""")]
    [InlineData("""{ "vehicle_id": "AMB-01", "rbk_report": {} }""")]
    [InlineData("""{ "vehicle_id": "AMB-01" }""")]
    [InlineData("""{ "vehicle_id": "AMB-01", "rbk_report": { "errors": "not-an-array" } }""")]
    public void Empty_absent_or_unusable_buckets_yield_no_alarms(string json)
    {
        Assert.Empty(ParseRobot(json));
    }

    [Fact]
    public void The_same_alarm_in_two_envelopes_is_reported_once()
    {
        // A robot entry may repeat a bucket at its own level and inside rbk_report.
        // Dedup is per (severity, code) so the operator does not see it twice.
        var alarms = ParseRobot(
            """
            {
              "vehicle_id": "AMB-01",
              "errors": [ { "code": 52201, "desc": "d", "times": 1 } ],
              "rbk_report": { "errors": [ { "code": 52201, "desc": "d", "times": 1 } ] }
            }
            """);

        Assert.Single(alarms);
    }

    [Fact]
    public void The_same_code_at_two_severities_stays_two_alarms()
    {
        var alarms = ParseRobot(
            """
            {
              "vehicle_id": "AMB-01",
              "rbk_report": {
                "errors": [ { "code": 52201, "desc": "d", "times": 1 } ],
                "warnings": [ { "code": 52201, "desc": "d", "times": 1 } ]
              }
            }
            """);

        Assert.Equal(2, alarms.Count);
        Assert.Contains(alarms, a => a.Severity == RdsAlarmSeverity.Error);
        Assert.Contains(alarms, a => a.Severity == RdsAlarmSeverity.Warning);
    }
}
