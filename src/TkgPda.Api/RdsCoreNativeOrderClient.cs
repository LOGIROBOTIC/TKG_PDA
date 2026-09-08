using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using TkgPda.Application;

namespace TkgPda.Api;

public sealed record RdsCoreExecutionOptions(
    bool Enabled,
    RdsExecutionMode Mode,
    string? BaseUrl,
    int TimeoutSeconds,
    bool AllowInsecureRemoteHttp = false)
{
    public static RdsCoreExecutionOptions FromConfiguration(IConfiguration configuration)
    {
        var modeText = configuration["RdsCoreExecution:Mode"] ?? "WIND_TASK";
        var mode = modeText.Trim().ToUpperInvariant() switch
        {
            "WIND_TASK" => RdsExecutionMode.WindTask,
            "RDSCORE_NATIVE_ORDER" => RdsExecutionMode.RdsCoreNativeOrder,
            _ => throw new InvalidOperationException("RdsCoreExecution:Mode must be WIND_TASK or RDSCORE_NATIVE_ORDER.")
        };
        var value = new RdsCoreExecutionOptions(
            configuration.GetValue("RdsCoreExecution:Enabled", false),
            mode,
            RdsCoreConfiguration.BaseUrl(configuration),
            RdsCoreConfiguration.TimeoutSeconds(configuration),
            RdsCoreConfiguration.AllowInsecureRemoteHttp(configuration));
        value.Validate();
        return value;
    }

    public Uri? BaseAddress => string.IsNullOrWhiteSpace(BaseUrl) ? null : new Uri(BaseUrl.Trim(), UriKind.Absolute);
    public bool MutationEnabled => Enabled && Mode == RdsExecutionMode.RdsCoreNativeOrder && BaseAddress is not null;

    public void Validate()
    {
        RdsCoreConfiguration.ValidateOrigin(BaseUrl, TimeoutSeconds, Enabled, AllowInsecureRemoteHttp, "execution");
        if (Enabled && Mode != RdsExecutionMode.RdsCoreNativeOrder)
            throw new InvalidOperationException("Enabled RDSCore execution requires explicit RDSCORE_NATIVE_ORDER mode.");
    }
}

internal static class RdsCoreConfiguration
{
    public static string? BaseUrl(IConfiguration configuration) =>
        configuration["RdsCore:BaseUrl"] ?? configuration["RdsCoreDiscovery:BaseUrl"];

    public static int TimeoutSeconds(IConfiguration configuration) =>
        configuration.GetValue<int?>("RdsCore:TimeoutSeconds")
        ?? configuration.GetValue("RdsCoreDiscovery:TimeoutSeconds", 5);

    public static bool AllowInsecureRemoteHttp(IConfiguration configuration) =>
        configuration["RdsCore:AllowInsecureRemoteHttp"] is not null
            ? configuration.GetValue("RdsCore:AllowInsecureRemoteHttp", false)
            : configuration.GetValue("RdsCoreDiscovery:AllowInsecureRemoteHttp", false);

    public static void ValidateOrigin(
        string? baseUrl,
        int timeoutSeconds,
        bool enabled,
        bool allowInsecureRemoteHttp,
        string purpose)
    {
        if (timeoutSeconds is < 1 or > 60)
            throw new InvalidOperationException($"RDSCore {purpose} timeout must be between 1 and 60 seconds.");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            if (enabled)
                throw new InvalidOperationException($"Enabled RDSCore {purpose} requires an explicit BaseURL.");
            return;
        }
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/")
        {
            throw new InvalidOperationException($"RDSCore {purpose} requires an explicit HTTP(S) origin without credentials, path, query, or fragment.");
        }
        if (enabled && uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback && !allowInsecureRemoteHttp)
            throw new InvalidOperationException($"RDSCore {purpose} refuses insecure non-loopback HTTP unless the explicit commissioning override is enabled.");
    }
}

public sealed record RdsCoreNativeOrderOperationDescriptor(string Operation, string Method, string Endpoint);

public sealed class RdsCoreNativeOrderClient(
    IHttpClientFactory httpClientFactory,
    RdsCoreExecutionOptions options) : IRdsCoreNativeOrderClient
{
    private enum Operation
    {
        SetOrder,
        OrderDetails,
        Orders,
        OrderDetailsByExternalId,
        OrderDetailsByBlockId,
        BlockDetailsById,
        Terminate,
        SetPriority,
        ClearRobotAllError,
        GotoSitePause,
        GotoSiteResume,
        ManualFinished,
        RedoFailedOrder,
        Dispatchable,
        RobotsStatus,
        Lock,
        Unlock,
        SwitchMap
    }

    private sealed record Spec(HttpMethod Method, string Path, bool Mutation);

    private static readonly IReadOnlyDictionary<Operation, Spec> Registry = new Dictionary<Operation, Spec>
    {
        [Operation.SetOrder] = new(HttpMethod.Post, "/setOrder", true),
        [Operation.OrderDetails] = new(HttpMethod.Get, "/orderDetails/{id}", false),
        [Operation.Orders] = new(HttpMethod.Get, "/orders", false),
        [Operation.OrderDetailsByExternalId] = new(HttpMethod.Get, "/orderDetailsByExternalId/{externalId}", false),
        [Operation.OrderDetailsByBlockId] = new(HttpMethod.Get, "/orderDetailsByBlockId/{blockId}", false),
        [Operation.BlockDetailsById] = new(HttpMethod.Get, "/blockDetailsById/{blockId}", false),
        [Operation.Terminate] = new(HttpMethod.Post, "/terminate", true),
        [Operation.SetPriority] = new(HttpMethod.Post, "/setPriority", true),
        [Operation.ClearRobotAllError] = new(HttpMethod.Post, "/clearRobotAllError", true),
        [Operation.GotoSitePause] = new(HttpMethod.Post, "/gotoSitePause", true),
        [Operation.GotoSiteResume] = new(HttpMethod.Post, "/gotoSiteResume", true),
        [Operation.ManualFinished] = new(HttpMethod.Post, "/manualFinished", true),
        [Operation.RedoFailedOrder] = new(HttpMethod.Post, "/redoFailedOrder", true),
        [Operation.Dispatchable] = new(HttpMethod.Post, "/dispatchable", true),
        [Operation.RobotsStatus] = new(HttpMethod.Get, "/robotsStatus", false),
        [Operation.Lock] = new(HttpMethod.Post, "/lock", true),
        [Operation.Unlock] = new(HttpMethod.Post, "/unlock", true),
        [Operation.SwitchMap] = new(HttpMethod.Post, "/switchMap", true)
    };

    public static IReadOnlyList<RdsCoreNativeOrderOperationDescriptor> AllowedOperations { get; } = Registry
        .OrderBy(item => item.Key)
        .Select(item => new RdsCoreNativeOrderOperationDescriptor(item.Key.ToString(), item.Value.Method.Method, item.Value.Path))
        .ToArray();

    public bool MutationEnabled => options.MutationEnabled;
    public RdsExecutionMode Mode => options.Mode;

    public async Task<RdsCoreSetOrderResult> SetOrderAsync(
        RdsCoreNormalOrder order,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        using var response = await SendAsync(Operation.SetOrder, BuildOrderPayload(order), null, correlationId, cancellationToken);
        var root = response.Document?.RootElement.Clone();
        var code = root is { } value ? Number(value, "code") : null;
        var diagnostics = root is { } result ? ParseDiagnostics(result) : RdsCoreOrderDiagnostics.Empty;
        var httpAccepted = response.StatusCode is >= 200 and <= 299;
        var coreAccepted = httpAccepted && code == 0;
        return new(
            httpAccepted,
            coreAccepted,
            coreAccepted && HasOrderDetails(root) && diagnostics.Errors.Count == 0,
            response.StatusCode,
            code,
            root is { } messageRoot ? Text(messageRoot, "msg") ?? Text(messageRoot, "message") : null,
            code == 50003,
            diagnostics,
            root);
    }

    public async Task<RdsCoreOrderDetails> GetOrderDetailsAsync(
        string orderId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(Operation.OrderDetails, null, ValidateToken(orderId, "order ID"), correlationId, cancellationToken);
        var root = RequireSuccessfulRead(response);
        return ParseOrder(FindSingleOrder(root));
    }

    public async Task<RdsCoreOrderList> GetOrdersAsync(
        RdsCoreOrderQuery query,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        var endpoint = string.Concat(
            "?page=", query.Page.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "&size=", query.Size.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "&orderBy=", SortField(query.SortBy),
            "&orderMethod=", query.SortDirection == RdsCoreOrderSortDirection.Ascending ? "ascending" : "descending");
        using var response = await SendAsync(Operation.Orders, null, null, correlationId, cancellationToken, endpoint);
        var root = RequireSuccessfulRead(response);
        var payload = UnwrapData(root);
        var array = FindArray(payload, "list");
        return new(
            array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Select(ParseOrder).ToArray(),
            Number(payload, "total") ?? Number(root, "total"),
            Number(payload, "page") ?? Number(payload, "currentPage"),
            Number(payload, "size"),
            root.Clone());
    }

    public async Task<IReadOnlyList<RdsCoreOrderDetails>> GetOrderDetailsByExternalIdAsync(
        string externalId,
        Guid correlationId,
        CancellationToken cancellationToken = default) =>
        await GetOrderArrayAsync(Operation.OrderDetailsByExternalId, ValidateToken(externalId, "external order ID"), correlationId, cancellationToken);

    public async Task<RdsCoreOrderDetails> GetOrderDetailsByBlockIdAsync(
        string blockId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(Operation.OrderDetailsByBlockId, null, ValidateToken(blockId, "block ID"), correlationId, cancellationToken);
        return ParseOrder(FindSingleOrder(RequireSuccessfulRead(response)));
    }

    public async Task<RdsCoreBlockDetails> GetBlockDetailsByIdAsync(
        string blockId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(Operation.BlockDetailsById, null, ValidateToken(blockId, "block ID"), correlationId, cancellationToken);
        var root = RequireSuccessfulRead(response);
        var payload = UnwrapData(root);
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("block", out var block)) payload = block;
        return ParseBlock(payload);
    }

    public async Task<RdsCoreTerminateResult> TerminateAsync(
        string orderId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var id = ValidateToken(orderId, "order ID");
        using var response = await SendAsync(
            Operation.Terminate,
            new { id, disableVehicle = false },
            null,
            correlationId,
            cancellationToken);
        var root = response.Document?.RootElement.Clone();
        var code = root is { } value ? Number(value, "code") : null;
        var httpAccepted = response.StatusCode is >= 200 and <= 299;
        return new(
            httpAccepted,
            httpAccepted && code == 0,
            response.StatusCode,
            code,
            root is { } messageRoot ? Text(messageRoot, "msg") ?? Text(messageRoot, "message") : null,
            root);
    }

    public async Task<RdsCoreSimpleResult> SetPriorityAsync(
        string orderId,
        int priority,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var id = ValidateToken(orderId, "order ID");
        using var response = await SendAsync(
            Operation.SetPriority,
            new { id, priority },
            null,
            correlationId,
            cancellationToken);
        return ParseSimpleResult(response);
    }

    public async Task<RdsCoreSimpleResult> ClearRobotAllErrorAsync(
        IReadOnlyList<string> vehicles,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var list = vehicles.Select(v => ValidateToken(v, "vehicle ID")).ToArray();
        using var response = await SendAsync(
            Operation.ClearRobotAllError,
            new { vehicles = list },
            null,
            correlationId,
            cancellationToken);
        return ParseSimpleResult(response);
    }

    public async Task<RdsCoreSimpleResult> GotoSitePauseAsync(
        IReadOnlyList<string> vehicles,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var list = vehicles.Select(v => ValidateToken(v, "vehicle ID")).ToArray();
        using var response = await SendAsync(
            Operation.GotoSitePause,
            new { vehicles = list },
            null,
            correlationId,
            cancellationToken);
        return ParseSimpleResult(response);
    }

    public async Task<RdsCoreSimpleResult> GotoSiteResumeAsync(
        IReadOnlyList<string> vehicles,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var list = vehicles.Select(v => ValidateToken(v, "vehicle ID")).ToArray();
        using var response = await SendAsync(
            Operation.GotoSiteResume,
            new { vehicles = list },
            null,
            correlationId,
            cancellationToken);
        return ParseSimpleResult(response);
    }

    public async Task<RdsCoreSimpleResult> ManualFinishedAsync(
        IReadOnlyList<string> vehicles,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var list = vehicles.Select(v => ValidateToken(v, "vehicle ID")).ToArray();
        using var response = await SendAsync(
            Operation.ManualFinished,
            new { vehicles = list },
            null,
            correlationId,
            cancellationToken);
        return ParseSimpleResult(response);
    }

    public async Task<RdsCoreSimpleResult> RedoFailedOrderAsync(
        IReadOnlyList<string> vehicles,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var list = vehicles.Select(v => ValidateToken(v, "vehicle ID")).ToArray();
        using var response = await SendAsync(
            Operation.RedoFailedOrder,
            new { vehicles = list },
            null,
            correlationId,
            cancellationToken);
        return ParseSimpleResult(response);
    }

    public async Task<RdsCoreSimpleResult> SetDispatchableAsync(
        IReadOnlyList<string> vehicles,
        string type,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var list = vehicles.Select(v => ValidateToken(v, "vehicle ID")).ToArray();
        var normalizedType = (type ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "dispatchable" => "dispatchable",
            "undispatchable_unignore" => "undispatchable_unignore",
            "undispatchable_ignore" => "undispatchable_ignore",
            _ => throw new ArgumentException("Dispatchable type must be 'dispatchable', 'undispatchable_unignore', or 'undispatchable_ignore'.", nameof(type))
        };
        using var response = await SendAsync(
            Operation.Dispatchable,
            new { vehicles = list, type = normalizedType },
            null,
            correlationId,
            cancellationToken);
        return ParseSimpleResult(response);
    }

    public async Task<IReadOnlyList<RdsCoreRobotTelemetry>> GetRobotsStatusAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            Operation.RobotsStatus,
            null,
            null,
            correlationId,
            cancellationToken);
        var root = RequireSuccessfulRead(response);
        return ParseRobotTelemetry(root);
    }

    public async Task<RdsCoreSimpleResult> LockAsync(
        IReadOnlyList<string> vehicles,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var list = vehicles.Select(v => ValidateToken(v, "vehicle ID")).ToArray();
        using var response = await SendAsync(
            Operation.Lock,
            new { vehicles = list },
            null,
            correlationId,
            cancellationToken);
        return ParseSimpleResult(response);
    }

    public async Task<RdsCoreSimpleResult> UnlockAsync(
        IReadOnlyList<string> vehicles,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var list = vehicles.Select(v => ValidateToken(v, "vehicle ID")).ToArray();
        using var response = await SendAsync(
            Operation.Unlock,
            new { vehicles = list },
            null,
            correlationId,
            cancellationToken);
        return ParseSimpleResult(response);
    }

    public async Task<RdsCoreSimpleResult> SwitchMapAsync(
        string vehicle,
        string map,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var v = ValidateToken(vehicle, "vehicle ID");
        var m = ValidateToken(map, "map name");
        using var response = await SendAsync(
            Operation.SwitchMap,
            new { vehicle = v, map = m },
            null,
            correlationId,
            cancellationToken);
        return ParseSimpleResult(response);
    }

    private async Task<IReadOnlyList<RdsCoreOrderDetails>> GetOrderArrayAsync(
        Operation operation,
        string token,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(operation, null, token, correlationId, cancellationToken);
        var root = RequireSuccessfulRead(response);
        var payload = UnwrapData(root);
        if (payload.ValueKind == JsonValueKind.Array)
            return payload.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Select(ParseOrder).ToArray();
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("orders", out var orders) && orders.ValueKind == JsonValueKind.Array)
            return orders.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Select(ParseOrder).ToArray();
        return payload.ValueKind == JsonValueKind.Object ? [ParseOrder(payload)] : [];
    }

    private async Task<RdsCoreHttpResponse> SendAsync(
        Operation operation,
        object? payload,
        string? pathToken,
        Guid correlationId,
        CancellationToken cancellationToken,
        string? queryString = null)
    {
        var spec = Registry[operation];
        if (spec.Mutation && !options.MutationEnabled)
            throw new RdsCoreNativeOrderException("RDSCORE_EXECUTION_DISABLED", "RDSCore native-order execution is disabled or is not the selected mode.");
        if (!spec.Mutation && (!options.Enabled || options.BaseAddress is null))
            throw new RdsCoreNativeOrderException("RDSCORE_READ_DISABLED", "RDSCore is disabled or has no BaseAddress configured.");
        var endpoint = (pathToken is null ? spec.Path : BindPath(spec.Path, pathToken)) + queryString;
        try
        {
            using var request = new HttpRequestMessage(spec.Method, endpoint);
            if (payload is not null) request.Content = JsonContent.Create(payload);
            request.Headers.TryAddWithoutValidation("language", "en");
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId.ToString("D"));
            using var response = await httpClientFactory.CreateClient("RdsCoreNativeOrder").SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            try
            {
                var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                return new((int)response.StatusCode, document);
            }
            catch (JsonException) when (!response.IsSuccessStatusCode)
            {
                return new((int)response.StatusCode, null);
            }
        }
        catch (RdsCoreNativeOrderException)
        {
            throw;
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RdsCoreNativeOrderException("RDSCORE_TIMEOUT", "RDSCore native-order request timed out.", error);
        }
        catch (HttpRequestException error)
        {
            throw new RdsCoreNativeOrderException("RDSCORE_UNAVAILABLE", "RDSCore native-order endpoint is unavailable.", error);
        }
        catch (JsonException error)
        {
            throw new RdsCoreNativeOrderException("RDSCORE_INVALID_JSON", "RDSCore returned invalid JSON.", error);
        }
    }

    private static object BuildOrderPayload(RdsCoreNormalOrder order)
    {
        var root = new JsonObject
        {
            ["id"] = order.Id,
            ["blocks"] = new JsonArray(order.Blocks.Select(BuildBlockPayload).ToArray()),
            ["complete"] = true
        };
        Add(root, "externalId", order.ExternalId);
        Add(root, "vehicle", order.Vehicle);
        Add(root, "group", order.Group);
        Add(root, "label", order.Label);
        if (order.KeyRoute is { Count: > 0 })
            root["keyRoute"] = new JsonArray(order.KeyRoute.Select(value => JsonValue.Create(value)).ToArray());
        Add(root, "keyTask", order.KeyTask);
        if (order.Priority is { } priority) root["priority"] = priority;
        return root;
    }

    private static JsonNode BuildBlockPayload(RdsCoreOrderBlock block)
    {
        var value = new JsonObject
        {
            ["blockId"] = block.BlockId,
            ["location"] = block.Location
        };
        switch (block)
        {
            case RdsCoreOperationBlock operation:
                value["operation"] = operation.Operation;
                Add(value, "operationArgs", operation.OperationArgs);
                Add(value, "postAction", operation.PostAction);
                break;
            case RdsCoreScriptBlock script:
                value["operation"] = "Script";
                value["scriptName"] = script.ScriptName;
                Add(value, "scriptArgs", script.ScriptArgs);
                Add(value, "postAction", script.PostAction);
                break;
            case RdsCoreBinTaskBlock binTask:
                value["binTask"] = binTask.BinTask;
                Add(value, "goodsId", binTask.GoodsId);
                Add(value, "postAction", binTask.PostAction);
                break;
        }
        return value;
    }

    private static void Add(JsonObject target, string name, string? value)
    {
        if (value is not null) target[name] = value;
    }

    private static void Add(JsonObject target, string name, JsonElement? value)
    {
        if (value is { } element) target[name] = JsonNode.Parse(element.GetRawText());
    }

    private static JsonElement RequireSuccessfulRead(RdsCoreHttpResponse response)
    {
        if (response.StatusCode is < 200 or > 299)
            throw new RdsCoreNativeOrderException("RDSCORE_HTTP_FAILURE", $"RDSCore returned HTTP {response.StatusCode}.");
        if (response.Document is null)
            throw new RdsCoreNativeOrderException("RDSCORE_INVALID_JSON", "RDSCore returned invalid JSON.");
        var root = response.Document.RootElement;
        var code = Number(root, "code");
        if (code is not null and not 0)
            throw new RdsCoreNativeOrderException("RDSCORE_BUSINESS_FAILURE", $"RDSCore rejected the read request with code {code}.");
        return root.Clone();
    }

    private static JsonElement FindSingleOrder(JsonElement root)
    {
        var payload = UnwrapData(root);
        foreach (var name in new[] { "order", "order_details", "orderDetails" })
        {
            if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object)
                return value;
        }
        return payload;
    }

    private static JsonElement UnwrapData(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data)
            ? data
            : root;

    private static JsonElement FindArray(JsonElement root, string name)
    {
        if (root.ValueKind == JsonValueKind.Array) return root;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array) return array;
        using var empty = JsonDocument.Parse("[]");
        return empty.RootElement.Clone();
    }

    private static RdsCoreOrderDetails ParseOrder(JsonElement value)
    {
        var blocks = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("blocks", out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Select(ParseBlock).ToArray()
            : [];
        return new(
            Text(value, "id"),
            Text(value, "externalId"),
            ParseOrderState(Text(value, "state") ?? Text(value, "status")),
            blocks,
            ParseDirectDiagnostics(value),
            value.Clone());
    }

    private static RdsCoreBlockDetails ParseBlock(JsonElement value) => new(
        Text(value, "blockId") ?? Text(value, "id"),
        Text(value, "location"),
        ParseBlockState(Text(value, "state") ?? Text(value, "status")),
        value.Clone());

    internal static RdsCoreOrderState ParseOrderState(string? raw) => new(raw switch
    {
        "CREATED" => RdsCoreKnownOrderState.Created,
        "WAITING" => RdsCoreKnownOrderState.Waiting,
        "RUNNING" => RdsCoreKnownOrderState.Running,
        "SUSPENDED" => RdsCoreKnownOrderState.Suspended,
        "FINISHED" => RdsCoreKnownOrderState.Finished,
        "FAILED" => RdsCoreKnownOrderState.Failed,
        "STOPPED" => RdsCoreKnownOrderState.Stopped,
        "Error" => RdsCoreKnownOrderState.Error,
        "TOBEDISPATCHED" => RdsCoreKnownOrderState.ToBeDispatched,
        _ => RdsCoreKnownOrderState.Unknown
    }, raw);

    internal static RdsCoreBlockState ParseBlockState(string? raw) => new(raw switch
    {
        "CREATED" => RdsCoreKnownBlockState.Created,
        "RUNNING" => RdsCoreKnownBlockState.Running,
        "WAITING" => RdsCoreKnownBlockState.Waiting,
        "POSTING" => RdsCoreKnownBlockState.Posting,
        "SUSPENDED" => RdsCoreKnownBlockState.Suspended,
        "FINISHED" => RdsCoreKnownBlockState.Finished,
        "FAILED" => RdsCoreKnownBlockState.Failed,
        "STOPPED" => RdsCoreKnownBlockState.Stopped,
        "MANUAL_FINISHED" => RdsCoreKnownBlockState.ManualFinished,
        "Error" => RdsCoreKnownBlockState.Error,
        _ => RdsCoreKnownBlockState.Unknown
    }, raw);

    private static RdsCoreOrderDiagnostics ParseDiagnostics(JsonElement root)
    {
        if (!TryOrderDetails(root, out var details)) return RdsCoreOrderDiagnostics.Empty;
        return new(ReadDiagnostics(details, "errors"), ReadDiagnostics(details, "warnings"), ReadDiagnostics(details, "notices"));
    }

    private static RdsCoreOrderDiagnostics ParseDirectDiagnostics(JsonElement value) => new(
        ReadDiagnostics(value, "errors"),
        ReadDiagnostics(value, "warnings"),
        ReadDiagnostics(value, "notices"));

    private static IReadOnlyList<RdsCoreDiagnostic> ReadDiagnostics(JsonElement details, string name)
    {
        if (details.ValueKind != JsonValueKind.Object || !details.TryGetProperty(name, out var values)) return [];
        if (values.ValueKind == JsonValueKind.Array)
            return values.EnumerateArray().Select(item => new RdsCoreDiagnostic(item.Clone())).ToArray();
        return values.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? []
            : [new(values.Clone())];
    }

    private static bool HasOrderDetails(JsonElement? root) => root is { } value && TryOrderDetails(value, out _);

    private static bool TryOrderDetails(JsonElement root, out JsonElement details)
    {
        var payload = UnwrapData(root);
        foreach (var name in new[] { "order_details", "orderDetails" })
        {
            if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out details) && details.ValueKind == JsonValueKind.Object)
                return true;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out details) && details.ValueKind == JsonValueKind.Object)
                return true;
        }
        details = default;
        return false;
    }

    private static int? Number(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property)) return null;
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number)
            ? number
            : int.TryParse(property.ValueKind == JsonValueKind.String ? property.GetString() : null, out number) ? number : null;
    }

    private static string? Text(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString()
            : null;

    private static string ValidateToken(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Any(char.IsControl))
            throw new ArgumentException($"A non-empty {name} within 200 characters is required.", nameof(value));
        return value.Trim();
    }

    private static string BindPath(string template, string token)
    {
        var start = template.IndexOf('{', StringComparison.Ordinal);
        var end = template.IndexOf('}', start + 1);
        if (start < 0 || end < 0) throw new InvalidOperationException("The native-order operation does not define a path token.");
        return string.Concat(template.AsSpan(0, start), Uri.EscapeDataString(token), template.AsSpan(end + 1));
    }

    private static string SortField(RdsCoreOrderSortField field) => field switch
    {
        RdsCoreOrderSortField.Id => "id",
        RdsCoreOrderSortField.State => "state",
        RdsCoreOrderSortField.CreateTime => "createTime",
        RdsCoreOrderSortField.TerminalTime => "terminalTime",
        RdsCoreOrderSortField.Vehicle => "vehicle",
        RdsCoreOrderSortField.ExternalId => "externalId",
        RdsCoreOrderSortField.Priority => "priority",
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static RdsCoreSimpleResult ParseSimpleResult(RdsCoreHttpResponse response)
    {
        var root = response.Document?.RootElement.Clone();
        var code = root is { } value ? Number(value, "code") : null;
        var httpAccepted = response.StatusCode is >= 200 and <= 299;
        return new(
            httpAccepted,
            httpAccepted && code == 0,
            response.StatusCode,
            code,
            root is { } messageRoot ? Text(messageRoot, "msg") ?? Text(messageRoot, "message") : null,
            root);
    }

    private static IReadOnlyList<RdsCoreRobotTelemetry> ParseRobotTelemetry(JsonElement root)
    {
        var target = root;
        if (target.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Object)
            target = dataProp;

        if (!target.TryGetProperty("report", out var report) || report.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<RdsCoreRobotTelemetry>();
        foreach (var robot in report.EnumerateArray())
        {
            if (robot.ValueKind != JsonValueKind.Object) continue;
            var identity = RdsRobotIdentity.FromRobotElement(robot);
            var vehicleId = identity.MutationTarget;
            if (string.IsNullOrWhiteSpace(vehicleId)) continue;

            var basic = Object(robot, "basic_info");
            var model = basic is { } b ? Text(b, "model") : null;
            var currentMap = basic is { } bm ? Text(bm, "current_map") : null;

            var rbk = Object(robot, "rbk_report");
            var currentStation = rbk is { } rcs ? Text(rcs, "current_station") : null;
            var lastStation = rbk is { } rls ? Text(rls, "last_station") : null;
            // Absence of battery telemetry is UNAVAILABLE (null), never a 0% reading.
            var batteryLevel = rbk is { } rbl && rbl.TryGetProperty("battery_level", out var bl) && bl.TryGetDouble(out var bld) ? bld : (double?)null;
            var x = rbk is { } rx && rx.TryGetProperty("x", out var xp) && xp.TryGetDouble(out var xd) ? xd : (double?)null;
            var y = rbk is { } ry && ry.TryGetProperty("y", out var yp) && yp.TryGetDouble(out var yd) ? yd : (double?)null;
            var angle = rbk is { } ra && ra.TryGetProperty("angle", out var ap) && ap.TryGetDouble(out var ad) ? ad : (double?)null;

            var connectionStatus = Number(robot, "connection_status") ?? 0;
            var dispatchable = Boolean(robot, "dispatchable");
            var isLoaded = Boolean(robot, "isLoaded");
            var isError = Boolean(robot, "is_error");

            var remainingTime = robot.TryGetProperty("remaining_time", out var rt) && rt.TryGetDouble(out var rtd) ? rtd : (double?)null;

            var currentOrder = Object(robot, "current_order");
            var currentOrderId = currentOrder is { } co ? Text(co, "id") : null;
            var currentOrderState = currentOrder is { } cos ? Text(cos, "state") : null;

            // Alarms come from the one shared parser: documented buckets only
            // (fatals/errors/warnings/notices), both entry shapes, severity kept.
            // This block previously looked for "alarms.fatals" and
            // "rbk_report.alarms.fatals" — paths that appear nowhere in the SEER
            // documentation — so it matched nothing on a real payload and every
            // robot reported zero alarms.
            var alarms = RdsRobotAlarmParser.ParseRobot(robot);

            // SEER idle test: dispatchable && !procBusiness. Absent means UNKNOWN.
            var procBusiness = robot.ValueKind == JsonValueKind.Object
                               && robot.TryGetProperty("procBusiness", out var pb)
                               && pb.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? pb.ValueKind == JsonValueKind.True
                : (bool?)null;

            var lockInfoObj = Object(robot, "lock_info") ?? (rbk is { } r ? Object(r, "lock_info") : null);
            RdsCoreRobotLockInfo? lockInfo = null;
            if (lockInfoObj is { } li)
            {
                var locked = Boolean(li, "locked");
                var ip = Text(li, "ip");
                var nickName = Text(li, "nick_name") ?? Text(li, "nickname");
                var port = Number(li, "port");
                var timeT = Text(li, "time_t");
                var type = Number(li, "type");
                var desc = Text(li, "desc");
                lockInfo = new RdsCoreRobotLockInfo(locked, ip, nickName, port, timeT, type, desc);
            }

            list.Add(new(
                vehicleId,
                model,
                currentMap,
                currentStation,
                lastStation,
                connectionStatus,
                dispatchable,
                isLoaded,
                isError,
                batteryLevel,
                x,
                y,
                angle,
                remainingTime,
                currentOrderId,
                currentOrderState,
                alarms,
                lockInfo,
                identity,
                procBusiness));
        }

        return list;
    }

    private static JsonElement? Object(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Object
            ? property
            : null;

    private static bool Boolean(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && (property.ValueKind == JsonValueKind.True
            || (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out var parsed) && parsed));

    private sealed record RdsCoreHttpResponse(int StatusCode, JsonDocument? Document) : IDisposable
    {
        public void Dispose() => Document?.Dispose();
    }
}

public sealed class RdsCoreRobotTelemetryProvider(
    IHttpClientFactory httpClientFactory,
    RdsCoreExecutionOptions options,
    ILogger<RdsCoreRobotTelemetryProvider> logger) : IRobotTelemetryProvider
{
    public async Task<IReadOnlyDictionary<string, double>> GetRobotRemainingTimesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled || options.BaseAddress is null)
            return new Dictionary<string, double>();

        try
        {
            var client = httpClientFactory.CreateClient("RdsCoreDiscovery");
            using var response = await client.GetAsync("/robotsStatus", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new Dictionary<string, double>();

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            var root = doc.RootElement;
            if (!root.TryGetProperty("report", out var report) || report.ValueKind != JsonValueKind.Array)
                return new Dictionary<string, double>();

            var result = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var robot in report.EnumerateArray())
            {
                if (robot.ValueKind != JsonValueKind.Object) continue;
                // Shared identity policy: the ETA map is keyed by the same
                // vehicle-space identifier that rds_tasks.agv_id and the mutation
                // APIs use, so enrichment cannot miss on an identity-space mismatch.
                var vehicleId = RdsRobotIdentity.FromRobotElement(robot).MutationTarget;
                if (string.IsNullOrWhiteSpace(vehicleId)) continue;

                if (robot.TryGetProperty("remaining_time", out var rt) && rt.TryGetDouble(out var rtd) && rtd >= 0)
                {
                    result[vehicleId] = rtd;
                }
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Robot telemetry fetch for remaining_time enrichment failed.");
            return new Dictionary<string, double>();
        }
    }
}
