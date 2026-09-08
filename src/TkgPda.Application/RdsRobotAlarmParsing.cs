using System.Text.Json;

namespace TkgPda.Application;

/// <summary>
/// SEER alarm severity, ordered least to most severe. Severity is structural, not
/// cosmetic: only <see cref="Fatal"/> and <see cref="Error"/> justify treating a
/// robot as faulted. A notice must never render or behave as a blocking alarm.
/// </summary>
public enum RdsAlarmSeverity
{
    Notice,
    Warning,
    Error,
    Fatal
}

/// <summary>One alarm entry with its severity preserved.</summary>
public sealed record RdsParsedAlarm(
    string Code,
    RdsAlarmSeverity Severity,
    string? Description,
    string? OccurredAt,
    int? Times)
{
    /// <summary>Fatal and error block dispatch. Warning and notice do not.</summary>
    public bool IsBlocking => Severity is RdsAlarmSeverity.Fatal or RdsAlarmSeverity.Error;
}

/// <summary>
/// The single SEER alarm parser. It previously existed twice — once correctly in the
/// read client and once, in the native telemetry client, reading
/// <c>alarms.fatals</c> and <c>rbk_report.alarms.fatals</c>. Neither of those paths
/// appears anywhere in the SEER documentation, so the native parser matched nothing
/// on a real payload: robots always reported zero alarms and the pre-dispatch
/// "robot has a blocking alarm" gate could not fire. Having one parser is what stops
/// the two from drifting apart again.
///
/// ENVELOPES (both documented, buckets are direct properties, always plural):
///   core level  — siblings of "report" on the response root
///                 (RdsScriptMethods_en.md:1040-1058)
///   robot level — inside each report[] entry's "rbk_report"
///                 (RdsScriptMethods_en.md:1135-1136, 1166, 1195)
///
/// ENTRY SHAPES (both documented, both occur):
///   A explicit code     { "code": 52201, "desc": "...", "times": 1, "timestamp": ... }
///                       (RdsScriptMethods_en.md:890-901)
///   B dynamically keyed { "55001": 1634020734, "desc": "...", "times": 1 }
///                       (RdsScriptMethods_en.md:1044-1056, 1195-1200)
///   In shape B the alarm code is the property NAME and its value is the occurrence
///   timestamp, so a parser requiring a "code" property silently drops every entry.
///
/// No undocumented envelope or bucket spelling is invented here.
/// </summary>
public static class RdsRobotAlarmParser
{
    private static readonly (string Bucket, RdsAlarmSeverity Severity)[] Buckets =
    [
        ("fatals", RdsAlarmSeverity.Fatal),
        ("errors", RdsAlarmSeverity.Error),
        ("warnings", RdsAlarmSeverity.Warning),
        ("notices", RdsAlarmSeverity.Notice)
    ];

    /// <summary>Sibling fields of an alarm entry that are never the alarm code.</summary>
    private static readonly HashSet<string> KnownAlarmFields =
        new(StringComparer.OrdinalIgnoreCase)
        { "code", "desc", "description", "times", "time", "timestamp", "level", "severity" };

    /// <summary>
    /// Parses every documented bucket found directly on <paramref name="container"/>.
    /// Entries are deduplicated on (severity, code) across repeated calls, so the same
    /// alarm appearing in two envelopes is reported once. A malformed entry is skipped
    /// individually — it never discards the rest of the bucket or the robot snapshot.
    /// </summary>
    public static void Append(List<RdsParsedAlarm> into, JsonElement container, HashSet<string> seen)
    {
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(seen);
        if (container.ValueKind != JsonValueKind.Object) return;

        foreach (var (bucketName, severity) in Buckets)
        {
            if (!container.TryGetProperty(bucketName, out var bucket) || bucket.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in bucket.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                var code = Text(item, "code");
                var occurredAt = Text(item, "time");

                if (string.IsNullOrEmpty(code))
                {
                    // Shape B: the single non-descriptive property is the alarm code
                    // and its value is the occurrence timestamp.
                    foreach (var property in item.EnumerateObject())
                    {
                        if (KnownAlarmFields.Contains(property.Name)) continue;
                        if (property.Value.ValueKind is not (JsonValueKind.Number or JsonValueKind.String)) continue;
                        code = property.Name;
                        occurredAt ??= property.Value.ValueKind == JsonValueKind.Number
                            ? property.Value.ToString()
                            : property.Value.GetString();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(code) || !seen.Add($"{severity}:{code}")) continue;

                into.Add(new RdsParsedAlarm(
                    code,
                    severity,
                    Text(item, "desc") ?? Text(item, "description"),
                    occurredAt ?? Text(item, "timestamp"),
                    Number(item, "times")));
            }
        }
    }

    /// <summary>
    /// Parses one report[] robot entry: the documented robot-level buckets inside
    /// "rbk_report", plus any carried directly on the entry in the same documented
    /// shape. Returns an empty list rather than throwing on unexpected input.
    /// </summary>
    public static IReadOnlyList<RdsParsedAlarm> ParseRobot(JsonElement robot)
    {
        var alarms = new List<RdsParsedAlarm>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Append(alarms, robot, seen);
        if (robot.ValueKind == JsonValueKind.Object
            && robot.TryGetProperty("rbk_report", out var rbkReport))
        {
            Append(alarms, rbkReport, seen);
        }

        return alarms;
    }

    private static string? Text(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property)) return null;
        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static int? Number(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property)) return null;
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number) ? number : null;
    }
}
