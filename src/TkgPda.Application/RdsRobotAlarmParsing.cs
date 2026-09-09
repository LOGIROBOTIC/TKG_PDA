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
/// ENVELOPES. Buckets are always plural (fatals/errors/warnings/notices) and appear
/// BOTH directly on a status object AND nested under an "alarms" wrapper. The single
/// documented /robotsStatus response carries both forms at once, so neither may be
/// dropped in favour of the other:
///
///   root, wrapped     alarms.{fatals,errors,warnings,notices}
///                     SEER_RDSCore_HTTP_API_Turkce_Ceviri_Tam.docx (extracted L4052)
///   root, direct      {fatals,errors,warnings,notices} as siblings of "alarms"
///                     same response, extracted L4079; RdsScriptMethods_en.md:1040-1058
///   robot, wrapped    report[].rbk_report.alarms.{...}
///                     docx extracted L4236 and L4427; the documented query path
///                     GET /robotsStatus?paths=report.rbk_report.alarms,report.vehicle_id
///                     (docx extracted L3593) returns exactly this shape
///   robot, direct     report[].rbk_report.{fatals,errors,...}
///                     RdsScriptMethods_en.md:1135-1136, 1166, 1195
///
/// A wrapped bucket may be JSON null rather than an empty array (docx L4443:
/// "alarms":{"errors":null,"fatals":null,"notices":null,"warnings":null}), which is
/// simply "no alarms" and must not throw.
///
/// A previous round concluded the "alarms" wrapper was undocumented because grep over
/// the Markdown files under RDS DÖKÜMANLARI/ found no hit. That was a search error,
/// not evidence: the authoritative DOCX in the repository root documents it plainly.
///
/// ENTRY SHAPES (both documented, both occur):
///   A explicit code     { "code": 52201, "desc": "...", "times": 1, "timestamp": ... }
///                       docx L4054-4058; RdsScriptMethods_en.md:890-901
///   B dynamically keyed { "55001": 1634020734, "desc": "...", "times": 1 }
///                       docx L4079-4083; RdsScriptMethods_en.md:1044-1056, 1195-1200
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
    /// Parses every documented bucket on <paramref name="container"/>, both the direct
    /// form and the "alarms" wrapper, because a single documented response carries both.
    /// Entries are deduplicated on (severity, code) across repeated calls, so the same
    /// alarm appearing in two envelopes is reported once. A malformed entry is skipped
    /// individually — it never discards the rest of the bucket or the robot snapshot.
    /// </summary>
    public static void Append(List<RdsParsedAlarm> into, JsonElement container, HashSet<string> seen)
    {
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(seen);
        if (container.ValueKind != JsonValueKind.Object) return;

        AppendBuckets(into, container, seen);

        // The wrapped form. Not an alternative to the direct form — the documented
        // /robotsStatus response has "alarms" and the bare buckets side by side.
        if (container.TryGetProperty("alarms", out var wrapper) && wrapper.ValueKind == JsonValueKind.Object)
        {
            AppendBuckets(into, wrapper, seen);
        }
    }

    private static void AppendBuckets(List<RdsParsedAlarm> into, JsonElement container, HashSet<string> seen)
    {
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
