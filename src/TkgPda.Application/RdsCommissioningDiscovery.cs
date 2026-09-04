using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TkgPda.Application;

public sealed record RdsCoreCaptureCommand(
    IReadOnlyList<RdsConnectivityProbe>? ConnectivityProbes = null,
    IReadOnlyList<RdsDistanceProbe>? DistanceProbes = null,
    IReadOnlyList<string>? OrderIds = null,
    IReadOnlyList<string>? ExternalOrderIds = null,
    IReadOnlyList<string>? BlockIds = null);

public sealed record RdsConnectivityProbe(string RobotGroup, IReadOnlyList<string> Points);
public sealed record RdsDistanceProbe(string RobotId, IReadOnlyList<string> Points);

public sealed record RdsCoreSourceSnapshot(
    string SafeOrigin,
    string? Version,
    IReadOnlyList<string> ObservedMaps,
    string? SceneMd5,
    string? ModelMd5);

public sealed record RdsCoreEndpointResult(
    string Operation,
    string Method,
    string Endpoint,
    string Status,
    int? HttpStatus,
    int? BusinessCode,
    string? ErrorCode,
    string? Message);

public sealed record RdsCoreAlarmSnapshot(
    string Scope,
    string Severity,
    string? Code,
    string? Description,
    int? OccurrenceCount,
    string? Timestamp);

public sealed record RdsCoreRobotSnapshot(
    string CanonicalId,
    string? Uuid,
    string? VehicleId,
    string? Group,
    IReadOnlyList<string> Labels,
    string? CurrentMap,
    bool? Dispatchable,
    int? ConnectionStatus,
    bool? ControlLocked,
    string? ControlOwnerType,
    double? BatteryLevel,
    bool? Charging,
    string? CurrentStation,
    double? X,
    double? Y,
    double? Angle,
    IReadOnlyList<string> ParkPoints,
    string? CurrentParkTarget,
    IReadOnlyList<RdsCoreAlarmSnapshot> Alarms,
    double? RemainingTime = null,
    string? CurrentOrderId = null,
    bool? IsLoaded = null,
    bool? ProcBusiness = null);

public sealed record RdsCoreBinTaskSnapshot(
    string Key,
    string? Operation,
    string? ScriptName,
    JsonElement? SafeArguments);

public sealed record RdsCoreWorkSiteSnapshot(
    string Id,
    bool? Exists,
    bool? Valid,
    string? PointName,
    JsonElement? Filled,
    JsonElement? Container,
    JsonElement? GoodsId,
    JsonElement? Loaded,
    IReadOnlyList<RdsCoreBinTaskSnapshot> BinTasks);

public sealed record RdsCoreDeviceSnapshot(
    string Kind,
    string Name,
    string? ConnectionStatus,
    bool? Available,
    bool? Disabled,
    JsonElement SafeState);

public sealed record RdsCoreChargeParameters(
    string RobotId,
    string? ChargeNeed,
    string? ChargeOnly,
    string? ChargedFull,
    string? ChargedOk);

public sealed record RdsCoreProbeSnapshot(
    string Kind,
    string Key,
    string Status,
    JsonElement? SafeResult);

public sealed record RdsCoreSelectedParametersSnapshot(
    string Status,
    IReadOnlyDictionary<string, JsonElement> Values);

public sealed record RdsCoreNavigationSnapshot(
    IReadOnlyList<string> DisabledPoints,
    IReadOnlyList<string> DisabledPaths,
    IReadOnlyList<RdsCoreProbeSnapshot> Connectivity,
    IReadOnlyList<RdsCoreProbeSnapshot> Distances);

public sealed record RdsCommissioningSnapshot(
    string SchemaVersion,
    DateTimeOffset CapturedAtUtc,
    Guid CorrelationId,
    RdsCoreSourceSnapshot Source,
    IReadOnlyList<RdsCoreEndpointResult> EndpointResults,
    IReadOnlyList<RdsCoreRobotSnapshot> Robots,
    IReadOnlyList<RdsCoreWorkSiteSnapshot> WorkSites,
    IReadOnlyList<RdsCoreDeviceSnapshot> Devices,
    RdsCoreNavigationSnapshot Navigation,
    IReadOnlyList<RdsCoreChargeParameters> ChargeParameters,
    RdsCoreSelectedParametersSnapshot SelectedCoreParameters,
    IReadOnlyList<RdsCoreProbeSnapshot> OrderProbes,
    IReadOnlyList<RdsCoreProbeSnapshot> BlockProbes);

public interface IRdsCoreDiscoveryClient
{
    bool Enabled { get; }
    string? SafeOrigin { get; }

    Task<RdsCommissioningSnapshot> CaptureAsync(
        RdsCoreCaptureCommand command,
        Guid correlationId,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed record RdsCommissioningSlotConfiguration(
    Guid SlotId,
    Guid LayoutId,
    Guid MachineId,
    string MachineCode,
    string LayoutCode,
    string SlotCode,
    string SlotRole,
    bool IsEnabled,
    string? RdsSiteId);

public sealed record RdsExpectedRobotMapping(string CanonicalRobotId, string? MachineCode);
public sealed record RdsRequiredBinTaskContract(string RdsSiteId, string BinTaskKey, string Operation);

public sealed record RdsCommissioningConfiguration(
    IReadOnlyList<RdsCommissioningSlotConfiguration> Slots,
    IReadOnlyList<RdsTaskTemplateSummary> TaskTemplates,
    IReadOnlyList<RdsExpectedRobotMapping> ExpectedRobots,
    IReadOnlyList<RdsRequiredBinTaskContract> RequiredBinTasks);

public interface IRdsCommissioningConfigurationStore
{
    Task<RdsCommissioningConfiguration> LoadAsync(CancellationToken cancellationToken = default);
}

public enum RdsCommissioningFindingClass
{
    Blocker,
    Gap,
    Warning,
    Info,
    NotEvaluated
}

public sealed record RdsCommissioningFinding(
    RdsCommissioningFindingClass Classification,
    string Code,
    string Subject,
    string Message);

public sealed record RdsCommissioningCaptureResult(
    RdsCommissioningSnapshot Snapshot,
    IReadOnlyList<RdsCommissioningFinding> Findings,
    string CanonicalJson,
    string SnapshotSha256,
    string MarkdownReport);

public sealed class RdsCommissioningDiscoveryService(
    IRdsCoreDiscoveryClient client,
    IRdsCommissioningConfigurationStore configurationStore,
    TimeProvider timeProvider)
{
    public async Task<RdsCommissioningCaptureResult> CaptureAsync(
        RdsCoreCaptureCommand command,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!client.Enabled || client.SafeOrigin is null)
        {
            throw new RdsCommissioningDiscoveryException(
                "RDSCORE_DISCOVERY_DISABLED",
                "RDSCore commissioning discovery is disabled or has no explicit BaseURL.");
        }

        var snapshot = await client.CaptureAsync(
            command,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        var configuration = await configurationStore.LoadAsync(cancellationToken);
        var findings = Compare(snapshot, configuration);
        var canonicalJson = RdsCommissioningCanonicalJson.Serialize(snapshot);
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
        var report = RdsCommissioningMarkdown.Render(snapshot, findings, sha);
        return new(snapshot, findings, canonicalJson, sha, report);
    }

    public static IReadOnlyList<RdsCommissioningFinding> Compare(
        RdsCommissioningSnapshot snapshot,
        RdsCommissioningConfiguration configuration)
    {
        var findings = new List<RdsCommissioningFinding>();
        var bins = snapshot.WorkSites.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var mappedSiteIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var slot in configuration.Slots
                     .Where(item => item.IsEnabled && item.SlotRole != "MACHINE")
                     .OrderBy(item => item.MachineCode, StringComparer.Ordinal)
                     .ThenBy(item => item.LayoutCode, StringComparer.Ordinal)
                     .ThenBy(item => item.SlotCode, StringComparer.Ordinal))
        {
            var subject = $"{slot.MachineCode}/{slot.LayoutCode}/{slot.SlotCode}";
            if (string.IsNullOrWhiteSpace(slot.RdsSiteId))
            {
                findings.Add(new(RdsCommissioningFindingClass.Gap, "TKG_SLOT_RDS_MAPPING_MISSING", subject,
                    "The enabled logistics slot has no explicit RDS WorkSite mapping."));
                continue;
            }

            mappedSiteIds.Add(slot.RdsSiteId);
            if (!bins.TryGetValue(slot.RdsSiteId, out var bin))
            {
                findings.Add(new(RdsCommissioningFindingClass.Blocker, "MAPPED_RDS_WORKSITE_ABSENT", subject,
                    $"Configured RDS WorkSite '{slot.RdsSiteId}' is absent from the captured bin inventory."));
            }
            else if (bin.Exists == false || bin.Valid == false)
            {
                findings.Add(new(RdsCommissioningFindingClass.Blocker, "MAPPED_RDS_WORKSITE_INVALID", subject,
                    $"Configured RDS WorkSite '{slot.RdsSiteId}' is reported as missing or invalid."));
            }

            if (bin?.PointName is { Length: > 0 } point
                && snapshot.Navigation.DisabledPoints.Contains(point, StringComparer.Ordinal))
            {
                findings.Add(new(RdsCommissioningFindingClass.Warning, "MAPPED_POINT_DISABLED", subject,
                    $"The mapped WorkSite point '{point}' is disabled in the snapshot."));
            }
        }

        foreach (var bin in snapshot.WorkSites.Where(item => !mappedSiteIds.Contains(item.Id)).OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            findings.Add(new(RdsCommissioningFindingClass.Info, "RDS_WORKSITE_UNMAPPED", bin.Id,
                "The discovered RDS WorkSite is not mapped to a TKG layout slot."));
        }

        if (configuration.ExpectedRobots.Count == 0)
        {
            findings.Add(new(RdsCommissioningFindingClass.NotEvaluated, "EXPECTED_ROBOT_MAPPING_UNDEFINED", "Robots",
                "No confirmed expected-robot mapping source exists; discovered robots are informational only."));
        }
        else
        {
            var discovered = snapshot.Robots.Select(item => item.CanonicalId).ToHashSet(StringComparer.Ordinal);
            foreach (var expected in configuration.ExpectedRobots.OrderBy(item => item.CanonicalRobotId, StringComparer.Ordinal))
            {
                if (!discovered.Contains(expected.CanonicalRobotId))
                {
                    findings.Add(new(RdsCommissioningFindingClass.Blocker, "EXPECTED_ROBOT_ABSENT", expected.CanonicalRobotId,
                        "The explicitly configured robot was not present in the RDSCore snapshot."));
                }
            }
        }

        if (configuration.RequiredBinTasks.Count == 0)
        {
            findings.Add(new(RdsCommissioningFindingClass.NotEvaluated, "BINTASK_CONTRACT_UNDEFINED", "binTask",
                "No operation-to-binTask commissioning contract is configured."));
        }
        else
        {
            foreach (var required in configuration.RequiredBinTasks.OrderBy(item => item.RdsSiteId, StringComparer.Ordinal).ThenBy(item => item.BinTaskKey, StringComparer.Ordinal))
            {
                if (!bins.TryGetValue(required.RdsSiteId, out var bin)
                    || !bin.BinTasks.Any(task => string.Equals(task.Key, required.BinTaskKey, StringComparison.Ordinal)))
                {
                    findings.Add(new(RdsCommissioningFindingClass.Blocker, "REQUIRED_BINTASK_ABSENT", required.RdsSiteId,
                        $"Explicit {required.Operation} contract requires binTask '{required.BinTaskKey}', but it was not discovered."));
                }
            }
        }

        if (snapshot.SelectedCoreParameters.Status == "NOT_CAPTURED")
        {
            findings.Add(new(RdsCommissioningFindingClass.NotEvaluated, "CORE_PARAMETER_ALLOWLIST_EMPTY", "Core parameters",
                "The approved Core parameter registry is empty; no Core parameters were requested."));
        }

        var operations = new[] { "BRING_EMPTY", "REMOVE_FULL", "REMOVE_EMPTY" };
        foreach (var operation in operations)
        {
            var templates = configuration.TaskTemplates.Where(item => item.IsActive && item.Operation == operation).ToArray();
            if (templates.Length == 0)
            {
                findings.Add(new(RdsCommissioningFindingClass.Gap, "PRODUCTION_TASK_CONTRACT_MISSING", operation,
                    "No active task template exists for this production operation."));
                continue;
            }

            foreach (var template in templates)
            {
                if (string.IsNullOrWhiteSpace(template.RdsTaskLabel)
                    || template.RdsTaskLabel.StartsWith("TKG_SIM_", StringComparison.Ordinal))
                {
                    findings.Add(new(RdsCommissioningFindingClass.Blocker, "PRODUCTION_TASK_LABEL_UNRESOLVED", operation,
                        "An active production template has no production RDS task label or references a commissioning-only label."));
                }
                if (template.Config.ValueKind != JsonValueKind.Object
                    || !template.Config.TryGetProperty("rdsInputBindings", out var bindings)
                    || bindings.ValueKind != JsonValueKind.Array
                    || bindings.GetArrayLength() == 0)
                {
                    findings.Add(new(RdsCommissioningFindingClass.Blocker, "PRODUCTION_TASK_BINDINGS_UNRESOLVED", operation,
                        "An active production template has no explicit RDS input bindings."));
                }
            }
        }

        return findings
            .OrderBy(item => item.Classification)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.Subject, StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed class RdsCommissioningDiscoveryException(string code, string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public string Code { get; } = code;
}

public static class RdsCommissioningCanonicalJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public static string Serialize<T>(T value)
    {
        var element = JsonSerializer.SerializeToElement(value, SerializerOptions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            WriteCanonical(writer, element);
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    public static JsonElement Canonicalize(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, value);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}

public static class RdsCommissioningMarkdown
{
    public static string Render(
        RdsCommissioningSnapshot snapshot,
        IReadOnlyList<RdsCommissioningFinding> findings,
        string sha256)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# RDS Commissioning Discovery Report");
        builder.AppendLine();
        builder.AppendLine($"- Captured: `{snapshot.CapturedAtUtc:O}`");
        builder.AppendLine($"- Correlation: `{snapshot.CorrelationId}`");
        builder.AppendLine($"- RDSCore origin: `{snapshot.Source.SafeOrigin}`");
        builder.AppendLine($"- Snapshot SHA-256: `{sha256}`");
        builder.AppendLine($"- Robots: {snapshot.Robots.Count}");
        builder.AppendLine($"- WorkSites/bins: {snapshot.WorkSites.Count}");
        builder.AppendLine();
        builder.AppendLine("RDS `filled`, container, goods and loaded telemetry remains raw RDS evidence and is not converted into TKG EMPTY/FULL state.");
        builder.AppendLine();
        builder.AppendLine("## Endpoint results");
        builder.AppendLine();
        builder.AppendLine("| Operation | Method | Endpoint | Status | HTTP | Business | Error |");
        builder.AppendLine("|---|---|---|---|---:|---:|---|");
        foreach (var item in snapshot.EndpointResults)
        {
            builder.AppendLine($"| {Escape(item.Operation)} | {item.Method} | `{Escape(item.Endpoint)}` | {item.Status} | {item.HttpStatus?.ToString() ?? ""} | {item.BusinessCode?.ToString() ?? ""} | {Escape(item.ErrorCode ?? "")} |");
        }
        builder.AppendLine();
        builder.AppendLine("## Findings");
        builder.AppendLine();
        foreach (var classification in Enum.GetValues<RdsCommissioningFindingClass>())
        {
            var rows = findings.Where(item => item.Classification == classification).ToArray();
            builder.AppendLine($"### {ToLabel(classification)} ({rows.Length})");
            builder.AppendLine();
            if (rows.Length == 0) builder.AppendLine("None.");
            foreach (var item in rows)
            {
                builder.AppendLine($"- `{item.Code}` — **{Escape(item.Subject)}**: {Escape(item.Message)}");
            }
            builder.AppendLine();
        }
        builder.AppendLine("## Preserved customer/site gaps");
        builder.AppendLine();
        builder.AppendLine("This discovery does not resolve the 48 Part↔Slot gaps, 74 container associations, SEALER V3 layout, EMPTY_STORAGE, REMOVE_FULL destination, container master gaps, 23 lot values, or HAOS/HMTR discrepancy.");
        return builder.ToString();
    }

    private static string ToLabel(RdsCommissioningFindingClass value) => value switch
    {
        RdsCommissioningFindingClass.NotEvaluated => "NOT_EVALUATED",
        _ => value.ToString().ToUpperInvariant()
    };

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
