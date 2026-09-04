using System.Net.Http.Json;
using System.Text.Json;
using TkgPda.Application;

namespace TkgPda.Api;

public sealed record RdsCoreDiscoveryOptions(
    bool Enabled,
    string? BaseUrl,
    int TimeoutSeconds,
    bool AllowInsecureRemoteHttp = false)
{
    public static RdsCoreDiscoveryOptions FromConfiguration(IConfiguration configuration)
    {
        var value = new RdsCoreDiscoveryOptions(
            configuration.GetValue("RdsCoreDiscovery:Enabled", false),
            RdsCoreConfiguration.BaseUrl(configuration),
            RdsCoreConfiguration.TimeoutSeconds(configuration),
            RdsCoreConfiguration.AllowInsecureRemoteHttp(configuration));
        value.Validate();
        return value;
    }

    public Uri? BaseAddress => string.IsNullOrWhiteSpace(BaseUrl) ? null : new Uri(BaseUrl.Trim(), UriKind.Absolute);

    public void Validate()
    {
        if (TimeoutSeconds is < 1 or > 60)
            throw new InvalidOperationException("RdsCore:TimeoutSeconds must be between 1 and 60.");
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (Enabled)
                throw new InvalidOperationException("Enabled RDSCore discovery requires an explicit BaseURL.");
            return;
        }
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/")
        {
            throw new InvalidOperationException("Enabled RDSCore discovery requires an explicit HTTP(S) origin without credentials, path, query, or fragment.");
        }
        if (Enabled && uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback && !AllowInsecureRemoteHttp)
            throw new InvalidOperationException("RDSCore discovery refuses insecure non-loopback HTTP unless the explicit commissioning override is enabled.");
    }
}

public sealed record RdsCoreDiscoveryOperationDescriptor(string Operation, string Method, string Endpoint, bool DefaultInventory);

public sealed class RdsCoreDiscoveryClient(
    IHttpClientFactory httpClientFactory,
    RdsCoreDiscoveryOptions options,
    ILogger<RdsCoreDiscoveryClient> logger) : IRdsCoreDiscoveryClient
{
    private enum Operation
    {
        RobotsStatus,
        BinCheck,
        DevicesDetails,
        RobotParkPoints,
        ChargeParameters,
        DisabledPoints,
        DisabledPaths,
        CoreParameters,
        Connectivity,
        Distance,
        OrderDetails,
        OrderDetailsByExternalId,
        BlockDetails
    }

    private sealed record Spec(HttpMethod Method, string Path, bool DefaultInventory);

    private static readonly IReadOnlyDictionary<Operation, Spec> Registry = new Dictionary<Operation, Spec>
    {
        [Operation.RobotsStatus] = new(HttpMethod.Get, "/robotsStatus", true),
        [Operation.BinCheck] = new(HttpMethod.Post, "/binCheck", true),
        [Operation.DevicesDetails] = new(HttpMethod.Get, "/devicesDetails", true),
        [Operation.RobotParkPoints] = new(HttpMethod.Post, "/getRobotParkPoints", true),
        [Operation.ChargeParameters] = new(HttpMethod.Get, "/getChargeParam", true),
        [Operation.DisabledPoints] = new(HttpMethod.Get, "/getDisablePoints", true),
        [Operation.DisabledPaths] = new(HttpMethod.Get, "/getDisablePaths", true),
        [Operation.CoreParameters] = new(HttpMethod.Post, "/getCoreParam", false),
        [Operation.Connectivity] = new(HttpMethod.Post, "/queryConnectivity", false),
        [Operation.Distance] = new(HttpMethod.Post, "/queryDist", false),
        [Operation.OrderDetails] = new(HttpMethod.Get, "/orderDetails/{id}", false),
        [Operation.OrderDetailsByExternalId] = new(HttpMethod.Get, "/orderDetailsByExternalId/{externalId}", false),
        [Operation.BlockDetails] = new(HttpMethod.Get, "/blockDetailsById/{blockId}", false)
    };

    // Intentionally empty until RDS/IT approves exact plugin/parameter pairs.
    private static readonly IReadOnlyList<(string Plugin, string Parameter)> ApprovedCoreParameters = [];

    public static IReadOnlyList<RdsCoreDiscoveryOperationDescriptor> AllowedOperations { get; } = Registry
        .OrderBy(item => item.Key)
        .Select(item => new RdsCoreDiscoveryOperationDescriptor(item.Key.ToString(), item.Value.Method.Method, item.Value.Path, item.Value.DefaultInventory))
        .ToArray();

    public bool Enabled => options.Enabled && options.BaseAddress is not null;
    public string? SafeOrigin => options.BaseAddress is { } uri
        ? $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? string.Empty : $":{uri.Port}")}"
        : null;

    public async Task<RdsCommissioningSnapshot> CaptureAsync(
        RdsCoreCaptureCommand command,
        Guid correlationId,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!Enabled)
            throw new RdsCommissioningDiscoveryException("RDSCORE_DISCOVERY_DISABLED", "RDSCore discovery is disabled or has no explicit BaseURL.");

        ValidateCommand(command);
        var endpointResults = new List<RdsCoreEndpointResult>();
        var robots = new List<RdsCoreRobotSnapshot>();
        var workSites = new List<RdsCoreWorkSiteSnapshot>();
        var devices = new List<RdsCoreDeviceSnapshot>();
        var charges = new List<RdsCoreChargeParameters>();
        var disabledPoints = new List<string>();
        var disabledPaths = new List<string>();
        var connectivity = new List<RdsCoreProbeSnapshot>();
        var distances = new List<RdsCoreProbeSnapshot>();
        var orders = new List<RdsCoreProbeSnapshot>();
        var blocks = new List<RdsCoreProbeSnapshot>();
        string? sceneMd5 = null;
        string? modelMd5 = null;
        string? version = null;

        var robotCall = await TryCallAsync(Operation.RobotsStatus, null, null, correlationId, cancellationToken);
        endpointResults.Add(robotCall.Result);
        if (robotCall.Data is { } robotData)
        {
            robots.AddRange(ParseRobots(robotData));
            sceneMd5 = Text(robotData, "scene_md5");
            modelMd5 = Text(robotData, "model_md5");
            version = Text(robotData, "version") ?? Text(robotData, "rds_version");
        }

        var binCall = await TryCallAsync(Operation.BinCheck, new { bins = Array.Empty<string>() }, null, correlationId, cancellationToken);
        endpointResults.Add(binCall.Result);
        if (binCall.Data is { } binData) workSites.AddRange(ParseBins(binData));

        var deviceCall = await TryCallAsync(Operation.DevicesDetails, null, null, correlationId, cancellationToken);
        endpointResults.Add(deviceCall.Result);
        if (deviceCall.Data is { } deviceData) devices.AddRange(ParseDevices(deviceData));

        var parkCall = await TryCallAsync(Operation.RobotParkPoints, new { vehicles = Array.Empty<string>() }, null, correlationId, cancellationToken);
        endpointResults.Add(parkCall.Result);
        if (parkCall.Data is { } parkData) robots = MergeParkPoints(robots, parkData).ToList();

        var chargeCall = await TryCallAsync(Operation.ChargeParameters, null, null, correlationId, cancellationToken);
        endpointResults.Add(chargeCall.Result);
        if (chargeCall.Data is { } chargeData) charges.AddRange(ParseChargeParameters(chargeData));

        var pointCall = await TryCallAsync(Operation.DisabledPoints, null, null, correlationId, cancellationToken);
        endpointResults.Add(pointCall.Result);
        if (pointCall.Data is { } pointData) disabledPoints.AddRange(ParseIdArray(pointData));

        var pathCall = await TryCallAsync(Operation.DisabledPaths, null, null, correlationId, cancellationToken);
        endpointResults.Add(pathCall.Result);
        if (pathCall.Data is { } pathData) disabledPaths.AddRange(ParseIdArray(pathData));

        var selectedParameters = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        if (ApprovedCoreParameters.Count > 0)
        {
            var grouped = ApprovedCoreParameters
                .GroupBy(item => item.Plugin, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(item => item.Parameter).OrderBy(item => item, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            var coreCall = await TryCallAsync(Operation.CoreParameters, new { param = grouped }, null, correlationId, cancellationToken);
            endpointResults.Add(coreCall.Result);
            if (coreCall.Data is { } coreData)
            {
                foreach (var plugin in coreData.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                    selectedParameters[plugin.Name] = Sanitize(plugin.Value);
            }
        }

        foreach (var probe in command.ConnectivityProbes ?? [])
        {
            var key = $"{probe.RobotGroup}:{string.Join(',', probe.Points.Order(StringComparer.Ordinal))}";
            var call = await TryCallAsync(Operation.Connectivity, new { robotGroup = probe.RobotGroup, points = probe.Points }, null, correlationId, cancellationToken);
            endpointResults.Add(call.Result with { Operation = $"Connectivity:{key}" });
            connectivity.Add(ToProbe("CONNECTIVITY", key, call));
        }

        foreach (var probe in command.DistanceProbes ?? [])
        {
            var key = $"{probe.RobotId}:{string.Join(',', probe.Points.Order(StringComparer.Ordinal))}";
            var payload = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [probe.RobotId] = probe.Points };
            var call = await TryCallAsync(Operation.Distance, payload, null, correlationId, cancellationToken);
            endpointResults.Add(call.Result with { Operation = $"Distance:{key}" });
            distances.Add(ToProbe("DISTANCE", key, call));
        }

        foreach (var id in command.OrderIds ?? [])
        {
            var call = await TryCallAsync(Operation.OrderDetails, null, id, correlationId, cancellationToken);
            endpointResults.Add(call.Result with { Operation = $"OrderDetails:{id}" });
            orders.Add(ToProbe("ORDER_ID", id, call));
        }
        foreach (var id in command.ExternalOrderIds ?? [])
        {
            var call = await TryCallAsync(Operation.OrderDetailsByExternalId, null, id, correlationId, cancellationToken);
            endpointResults.Add(call.Result with { Operation = $"ExternalOrderId:{id}" });
            orders.Add(ToProbe("EXTERNAL_ORDER_ID", id, call));
        }
        foreach (var id in command.BlockIds ?? [])
        {
            var call = await TryCallAsync(Operation.BlockDetails, null, id, correlationId, cancellationToken);
            endpointResults.Add(call.Result with { Operation = $"BlockDetails:{id}" });
            blocks.Add(ToProbe("BLOCK_ID", id, call));
        }

        var maps = robots.Select(item => item.CurrentMap).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        logger.LogInformation(
            "RDSCore discovery completed with {EndpointCount} endpoint results, {RobotCount} robots, {WorkSiteCount} bins and correlation {CorrelationId}.",
            endpointResults.Count, robots.Count, workSites.Count, correlationId);

        return new(
            "rds_commissioning_snapshot_v1",
            capturedAtUtc,
            correlationId,
            new(SafeOrigin!, version, maps, sceneMd5, modelMd5),
            endpointResults.OrderBy(item => item.Operation, StringComparer.Ordinal).ToArray(),
            robots.OrderBy(item => item.CanonicalId, StringComparer.Ordinal).ToArray(),
            workSites.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            devices.OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Name, StringComparer.Ordinal).ToArray(),
            new(
                disabledPoints.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                disabledPaths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                connectivity.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray(),
                distances.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray()),
            charges.OrderBy(item => item.RobotId, StringComparer.Ordinal).ToArray(),
            new(ApprovedCoreParameters.Count == 0 ? "NOT_CAPTURED" : "CAPTURED", selectedParameters),
            orders.OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Key, StringComparer.Ordinal).ToArray(),
            blocks.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray());
    }

    private async Task<CallResult> TryCallAsync(
        Operation operation,
        object? payload,
        string? pathToken,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var spec = Registry[operation];
        var endpoint = pathToken is null ? spec.Path : BindPathToken(spec.Path, pathToken);
        try
        {
            using var request = new HttpRequestMessage(spec.Method, endpoint);
            if (payload is not null) request.Content = JsonContent.Create(payload);
            request.Headers.TryAddWithoutValidation("language", "en");
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId.ToString());
            using var response = await RdsHttpReadTransport.SendAsync(httpClientFactory, "RdsCoreDiscovery", request, cancellationToken);
            var businessCode = response.Document is null ? null : BusinessCode(response.Document.RootElement);
            var message = response.Document is null ? null : Text(response.Document.RootElement, "msg");
            if (response.StatusCode is < 200 or > 299)
            {
                var status = response.StatusCode == 404 ? "NOT_FOUND" : "FAILED";
                return new(null, new(operation.ToString(), spec.Method.Method, endpoint, status, response.StatusCode, businessCode,
                    response.StatusCode == 404 ? "RDSCORE_NOT_FOUND" : "RDSCORE_HTTP_FAILURE", message));
            }
            if (businessCode is not null and not 0)
            {
                return new(null, new(operation.ToString(), spec.Method.Method, endpoint, "FAILED", response.StatusCode, businessCode,
                    "RDSCORE_BUSINESS_FAILURE", message));
            }
            return new(response.Document!.RootElement.Clone(), new(operation.ToString(), spec.Method.Method, endpoint, "SUCCESS", response.StatusCode, businessCode, null, message));
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            return new(null, new(operation.ToString(), spec.Method.Method, endpoint, "FAILED", null, null, "RDSCORE_TIMEOUT", error.Message));
        }
        catch (HttpRequestException error)
        {
            return new(null, new(operation.ToString(), spec.Method.Method, endpoint, "FAILED", null, null, "RDSCORE_UNAVAILABLE", error.Message));
        }
        catch (JsonException error)
        {
            return new(null, new(operation.ToString(), spec.Method.Method, endpoint, "FAILED", null, null, "RDSCORE_INVALID_JSON", error.Message));
        }
    }

    private static IReadOnlyList<RdsCoreRobotSnapshot> ParseRobots(JsonElement root)
    {
        if (!root.TryGetProperty("report", out var report) || report.ValueKind != JsonValueKind.Array) return [];
        var rows = new List<RdsCoreRobotSnapshot>();
        foreach (var robot in report.EnumerateArray())
        {
            if (robot.ValueKind != JsonValueKind.Object) continue;
            var id = Text(robot, "uuid") ?? Text(robot, "vehicle_id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            var uuid = Text(robot, "uuid");
            var vehicleId = Text(robot, "vehicle_id");
            var basic = Object(robot, "basic_info");
            var telemetry = Object(robot, "rbk_report");
            var lockInfo = Object(robot, "lock_info");
            var labels = Strings(robot, "labels").Concat(Strings(basic, "current_label")).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var alarms = ParseAlarmBuckets(Object(root, "alarms"), "CORE")
                .Concat(ParseAlarmBuckets(telemetry, "ROBOT"))
                .Concat(ParseAlarmBuckets(Object(telemetry, "alarms"), "ROBOT"))
                .OrderBy(item => item.Scope, StringComparer.Ordinal).ThenBy(item => item.Severity, StringComparer.Ordinal).ThenBy(item => item.Code, StringComparer.Ordinal).ToArray();
            var currentOrder = Object(robot, "current_order");
            var currentOrderId = Text(currentOrder, "id") ?? Text(currentOrder, "externalId");
            var remainingTime = Double(robot, "remaining_time");
            var isLoaded = Boolean(robot, "isLoaded");
            var procBusiness = Boolean(robot, "procBusiness");
            rows.Add(new(
                id,
                uuid,
                vehicleId,
                Text(basic, "current_group"),
                labels,
                Text(telemetry, "current_map") ?? Text(basic, "current_map"),
                Boolean(robot, "dispatchable"),
                Integer(robot, "connection_status"),
                Boolean(lockInfo, "locked"),
                Text(lockInfo, "type"),
                Double(telemetry, "battery_level"),
                Boolean(telemetry, "charging"),
                Text(telemetry, "current_station"),
                Double(telemetry, "x"),
                Double(telemetry, "y"),
                Double(telemetry, "angle"),
                [],
                null,
                alarms,
                remainingTime,
                currentOrderId,
                isLoaded,
                procBusiness));
        }
        return rows;
    }

    private static IEnumerable<RdsCoreAlarmSnapshot> ParseAlarmBuckets(JsonElement value, string scope)
    {
        if (value.ValueKind != JsonValueKind.Object) yield break;
        foreach (var bucket in new[] { ("fatals", "fatal"), ("errors", "error"), ("warnings", "warning"), ("notices", "notice") })
        {
            if (!value.TryGetProperty(bucket.Item1, out var rows) || rows.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in rows.EnumerateArray())
            {
                yield return new(scope, bucket.Item2, Text(item, "code"), Text(item, "desc") ?? Text(item, "describe"), Integer(item, "times"), Text(item, "dataTime") ?? Text(item, "timestamp"));
            }
        }
    }

    private static IReadOnlyList<RdsCoreWorkSiteSnapshot> ParseBins(JsonElement root)
    {
        if (!root.TryGetProperty("bins", out var bins) || bins.ValueKind != JsonValueKind.Array) return [];
        var rows = new List<RdsCoreWorkSiteSnapshot>();
        foreach (var bin in bins.EnumerateArray())
        {
            var id = Text(bin, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            var status = Object(bin, "status");
            var tasks = new List<RdsCoreBinTaskSnapshot>();
            if (status.ValueKind == JsonValueKind.Object && status.TryGetProperty("binTask", out var taskArray) && taskArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in taskArray.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    foreach (var task in item.EnumerateObject().OrderBy(value => value.Name, StringComparer.Ordinal))
                    {
                        tasks.Add(new(task.Name, Text(task.Value, "operation"), Text(task.Value, "script_name") ?? Text(task.Value, "scriptName"), Sanitize(task.Value)));
                    }
                }
            }
            rows.Add(new(
                id,
                Boolean(bin, "exist"),
                Boolean(bin, "valid"),
                Text(status, "point_name"),
                Property(status, bin, "filled"),
                Property(status, bin, "container"),
                Property(status, bin, "goodsId"),
                Property(status, bin, "loaded"),
                tasks.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray()));
        }
        return rows;
    }

    private static IReadOnlyList<RdsCoreDeviceSnapshot> ParseDevices(JsonElement root)
    {
        var rows = new List<RdsCoreDeviceSnapshot>();
        foreach (var descriptor in new[] { ("doors", "DOOR"), ("lifts", "LIFT"), ("terminals", "TERMINAL"), ("wind_showers", "WIND_SHOWER"), ("windshowers", "WIND_SHOWER") })
        {
            if (!root.TryGetProperty(descriptor.Item1, out var array) || array.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in array.EnumerateArray())
            {
                var name = Text(item, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                rows.Add(new(descriptor.Item2, name, Text(item, "connection_status"), Boolean(item, "available"), Boolean(item, "forbidden"), Sanitize(item)));
            }
        }
        return rows;
    }

    private static IReadOnlyList<RdsCoreRobotSnapshot> MergeParkPoints(IReadOnlyList<RdsCoreRobotSnapshot> robots, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) return robots;
        var parks = root.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object && !string.IsNullOrWhiteSpace(Text(item, "vehicle")))
            .ToDictionary(item => Text(item, "vehicle")!, item => (Points: Strings(item, "parkPoints"), Target: Text(item, "currentTarget")), StringComparer.Ordinal);
        return robots.Select(robot =>
        {
            if (!parks.TryGetValue(robot.CanonicalId, out var park)
                && (robot.VehicleId is null || !parks.TryGetValue(robot.VehicleId, out park))) return robot;
            return robot with { ParkPoints = park.Points.Order(StringComparer.Ordinal).ToArray(), CurrentParkTarget = park.Target };
        }).ToArray();
    }

    private static IReadOnlyList<RdsCoreChargeParameters> ParseChargeParameters(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return [];
        return root.EnumerateObject()
            .Where(item => item.Value.ValueKind == JsonValueKind.Object && item.Name is not ("code" or "msg" or "create_on"))
            .Select(item => new RdsCoreChargeParameters(item.Name, Text(item.Value, "chargeNeed"), Text(item.Value, "chargeOnly"), Text(item.Value, "chargedFull"), Text(item.Value, "chargedOK")))
            .OrderBy(item => item.RobotId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ParseIdArray(JsonElement root) => root.ValueKind == JsonValueKind.Array
        ? root.EnumerateArray().Select(item => Text(item, "id")).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
        : [];

    private static RdsCoreProbeSnapshot ToProbe(string kind, string key, CallResult call) =>
        new(kind, key, call.Result.Status, call.Data is { } data ? Sanitize(data) : null);

    private static int? BusinessCode(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("code", out var value)) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : int.TryParse(value.ValueKind == JsonValueKind.String ? value.GetString() : null, out number) ? number : null;
    }

    private static void ValidateCommand(RdsCoreCaptureCommand command)
    {
        ValidateValues(command.OrderIds, "order ID");
        ValidateValues(command.ExternalOrderIds, "external order ID");
        ValidateValues(command.BlockIds, "block ID");
        foreach (var probe in command.ConnectivityProbes ?? [])
        {
            ValidateValue(probe.RobotGroup, "robot group");
            ValidateValues(probe.Points, "point");
        }
        foreach (var probe in command.DistanceProbes ?? [])
        {
            ValidateValue(probe.RobotId, "robot ID");
            ValidateValues(probe.Points, "point");
        }
    }

    private static string BindPathToken(string template, string value)
    {
        var start = template.IndexOf('{', StringComparison.Ordinal);
        var end = start < 0 ? -1 : template.IndexOf('}', start + 1);
        if (start < 0 || end < 0)
            throw new InvalidOperationException("The closed discovery operation does not define a path token.");
        return string.Concat(template.AsSpan(0, start), Uri.EscapeDataString(value), template.AsSpan(end + 1));
    }

    private static void ValidateValues(IReadOnlyList<string>? values, string name)
    {
        if (values is null) return;
        if (values.Count > 100) throw new RdsCommissioningDiscoveryException("RDSCORE_PROBE_LIMIT_EXCEEDED", $"At most 100 {name} values are supported.");
        foreach (var value in values) ValidateValue(value, name);
    }

    private static void ValidateValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Any(char.IsControl))
            throw new RdsCommissioningDiscoveryException("RDSCORE_PROBE_INVALID", $"A {name} value is empty or outside the supported format.");
    }

    private static JsonElement Sanitize(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteSanitized(writer, value);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteSanitized(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                if (IsSecretName(property.Name)) writer.WriteStringValue("[REDACTED]");
                else WriteSanitized(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) WriteSanitized(writer, item);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }

    private static bool IsSecretName(string name)
    {
        var normalized = name.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("passwd", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("credential", StringComparison.Ordinal)
            || normalized.Contains("authorization", StringComparison.Ordinal)
            || normalized.Contains("apikey", StringComparison.Ordinal)
            || normalized.Contains("privatekey", StringComparison.Ordinal)
            || normalized.Contains("integrationkey", StringComparison.Ordinal)
            || normalized.Contains("callbackkey", StringComparison.Ordinal);
    }

    private static JsonElement Object(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    private static JsonElement? Property(JsonElement primary, JsonElement fallback, string name)
    {
        if (primary.ValueKind == JsonValueKind.Object && primary.TryGetProperty(name, out var value)) return Sanitize(value);
        if (fallback.ValueKind == JsonValueKind.Object && fallback.TryGetProperty(name, out value)) return Sanitize(value);
        return null;
    }

    private static string? Text(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool? Boolean(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when value.TryGetInt32(out var number) => number != 0,
                JsonValueKind.String when bool.TryParse(value.GetString(), out var boolean) => boolean,
                _ => null
            }
            : null;

    private static int? Integer(JsonElement parent, string name) =>
        int.TryParse(Text(parent, name), out var value) ? value : null;

    private static double? Double(JsonElement parent, string name) =>
        double.TryParse(Text(parent, name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    private static IReadOnlyList<string> Strings(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(item => item is not null).Cast<string>().ToArray()
            : [];

    private sealed record CallResult(JsonElement? Data, RdsCoreEndpointResult Result);
}
