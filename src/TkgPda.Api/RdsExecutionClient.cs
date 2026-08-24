using System.Net.Http.Json;
using System.Text.Json;
using TkgPda.Application;

namespace TkgPda.Api;

public sealed class RdsExecutionClient(
    IHttpClientFactory httpClientFactory,
    RdsOptions options) : IRdsExecutionClient
{
    public bool DispatchEnabled => options.Enabled && options.DispatchEnabled;

    public async Task<RdsValidationResult> ValidateTransportAsync(
        RdsValidateTransportRequest request,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var root = await PostAsync(
            "/script-api/tkg/validate-transport",
            new
            {
                integrationKey = options.IntegrationKey,
                request.TaskRecordId,
                request.TaskLabel,
                inputParams = request.InputParams
            },
            correlationId,
            cancellationToken,
            requireDispatch: false);
        var data = Data(root);
        return new(
            Boolean(data, "validated"),
            Boolean(data, "taskLabelExists"),
            Boolean(data, "taskRecordExists"),
            Number(root, "code") ?? Number(data, "code") ?? 500,
            Text(root, "message") ?? Text(root, "msg") ?? Text(data, "message") ?? string.Empty,
            root.Clone());
    }

    public async Task<RdsCreateTransportResult> CreateTransportAsync(
        RdsCreateTransportRequest request,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var root = await PostScriptAsync(
            "/script-api/tkg/create-transport",
            new
            {
                integrationKey = options.IntegrationKey,
                request.TaskRecordId,
                request.TaskLabel,
                request.Priority,
                inputParams = request.InputParams
            },
            correlationId,
            cancellationToken);
        var data = Data(root);
        var code = Number(root, "code") ?? Number(data, "code") ?? 500;
        return new(
            code == 200,
            Boolean(data, "idempotentReplay") || Boolean(root, "idempotentReplay"),
            code,
            Text(root, "message") ?? Text(root, "msg") ?? Text(data, "message") ?? string.Empty,
            root.Clone());
    }

    public async Task<RdsStopTaskResult> StopTaskAsync(string taskRecordId, Guid correlationId, CancellationToken cancellationToken = default)
    {
        var root = await PostAsync(
            "/api/stop-task",
            new { releaseSite = 1, taskRecordId },
            correlationId,
            cancellationToken,
            requireDispatch: true);
        var code = Number(root, "code") ?? 500;
        if (code != 200)
        {
            throw new RdsExecutionException("RDS_STOP_REJECTED", "RDS rejected the stop-task request.");
        }
        return new(code, Text(root, "msg") ?? string.Empty, root.Clone());
    }

    public async Task<RdsContentUpdateResult> UpdateContentAsync(string siteId, string content, Guid correlationId, CancellationToken cancellationToken = default)
    {
        var root = await PostScriptAsync(
            "/script-api/tkg/update-content",
            new { integrationKey = options.IntegrationKey, siteId, content },
            correlationId,
            cancellationToken);
        var data = Data(root);
        var code = Number(root, "code") ?? Number(data, "code") ?? 500;
        if (code != 200)
        {
            throw new RdsExecutionException("RDS_CONTENT_UPDATE_REJECTED", "RDS rejected the controlled content update.");
        }
        return new(code, Text(root, "message") ?? Text(root, "msg") ?? string.Empty, root.Clone());
    }

    private async Task<JsonElement> PostScriptAsync(string endpoint, object payload, Guid correlationId, CancellationToken cancellationToken) =>
        await PostAsync(endpoint, payload, correlationId, cancellationToken, requireDispatch: true);

    private async Task<JsonElement> PostAsync(
        string endpoint,
        object payload,
        Guid correlationId,
        CancellationToken cancellationToken,
        bool requireDispatch)
    {
        if (!options.Enabled)
        {
            throw new RdsExecutionException("RDS_DISABLED", "RDS integration is disabled.");
        }

        if (requireDispatch && !DispatchEnabled)
        {
            throw new RdsExecutionException("RDS_DISPATCH_DISABLED", "RDS mutation is disabled.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId.ToString());
            request.Headers.TryAddWithoutValidation("language", "en");
            using var response = await httpClientFactory.CreateClient("RdsMutation").SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new RdsExecutionException("RDS_HTTP_FAILURE", $"RDS returned HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = NormalizeEnvelope(UnwrapJson(document.RootElement));
            var code = Number(root, "code") ?? Number(Data(root), "code");
            if (code is not null and not 200)
            {
                throw new RdsExecutionException("RDS_BUSINESS_FAILURE", "RDS rejected the mutation request.");
            }
            return root;
        }
        catch (RdsExecutionException)
        {
            throw;
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RdsExecutionException("RDS_TIMEOUT", "RDS mutation timed out.", error);
        }
        catch (HttpRequestException error)
        {
            throw new RdsExecutionException("RDS_UNAVAILABLE", "RDS mutation endpoint is unavailable.", error);
        }
        catch (JsonException error)
        {
            throw new RdsExecutionException("RDS_INVALID_JSON", "RDS returned invalid JSON.", error);
        }
    }

    private static JsonElement Data(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data)) return root;
        var unwrapped = UnwrapJson(data);
        return unwrapped.ValueKind == JsonValueKind.Object ? unwrapped : root;
    }

    private static JsonElement UnwrapJson(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) return value.Clone();
        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text)) return value.Clone();
        try
        {
            using var nested = JsonDocument.Parse(text);
            return nested.RootElement.Clone();
        }
        catch (JsonException)
        {
            return value.Clone();
        }
    }

    private static JsonElement NormalizeEnvelope(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.String)
        {
            var inner = UnwrapJson(data);
            if (inner.ValueKind == JsonValueKind.Object && Number(inner, "code") is not null)
            {
                return inner;
            }
        }
        return root;
    }

    private static string? Text(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property)
            ? property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString()
            : null;

    private static int? Number(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property)) return null;
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number)
            ? number
            : int.TryParse(property.ValueKind == JsonValueKind.String ? property.GetString() : null, out number) ? number : null;
    }

    private static bool Boolean(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && (property.ValueKind == JsonValueKind.True
            || property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out var parsed) && parsed);
}

public sealed class RdsExecutionWorker(
    RdsExecutionCoordinator coordinator,
    RdsOptions options,
    ILogger<RdsExecutionWorker> logger) : BackgroundService
{
    private readonly SemaphoreSlim gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled || !options.DispatchEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.WorkerIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            if (await gate.WaitAsync(0, stoppingToken))
            {
                try
                {
                    await coordinator.DispatchQueuedAsync(20, stoppingToken);
                    await coordinator.ReconcileAsync(100, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (RdsExecutionException error)
                {
                    logger.LogWarning("RDS execution cycle was recoverably blocked with {Code}.", error.Code);
                }
                catch (RdsReadException error)
                {
                    logger.LogWarning("RDS reconciliation read was recoverably blocked with {Code}.", error.Code);
                }
                finally
                {
                    gate.Release();
                }
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken)) return;
        }
    }
}
