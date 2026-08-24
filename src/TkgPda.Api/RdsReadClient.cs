using System.Net.Http.Json;
using System.Text.Json;
using TkgPda.Application;

namespace TkgPda.Api;

public sealed record RdsOptions(bool Enabled, string? BaseUrl, int TimeoutSeconds)
{
    public static RdsOptions FromConfiguration(IConfiguration configuration)
    {
        var options = new RdsOptions(
            configuration.GetValue("Rds:Enabled", false),
            configuration["Rds:BaseUrl"],
            configuration.GetValue("Rds:TimeoutSeconds", 5));
        options.Validate();
        return options;
    }

    public Uri? BaseAddress => string.IsNullOrWhiteSpace(BaseUrl) ? null : new Uri(BaseUrl.Trim(), UriKind.Absolute);

    public void Validate()
    {
        if (TimeoutSeconds is < 1 or > 60)
        {
            throw new InvalidOperationException("Rds:TimeoutSeconds must be between 1 and 60.");
        }

        if (!Enabled)
        {
            return;
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("Enabled RDS integration requires an absolute HTTP(S) BaseUrl without embedded credentials.");
        }
    }
}

public sealed class RdsReadClient(
    IHttpClientFactory httpClientFactory,
    RdsOptions options,
    TimeProvider timeProvider,
    ILogger<RdsReadClient> logger) : IRdsReadClient
{
    private const string WorkSitesEndpoint = "/api/work-sites/sites";
    private const string WindTasksEndpoint = "/api/queryWindTask";
    private readonly object lastReadLock = new();
    private DateTimeOffset? lastSuccessfulRead;

    public bool Enabled => options.Enabled;
    public string? SafeBaseAddress => options.BaseAddress is { } uri
        ? $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? string.Empty : $":{uri.Port}")}"
        : null;
    public DateTimeOffset? LastSuccessfulRead
    {
        get { lock (lastReadLock) return lastSuccessfulRead; }
    }

    public async Task<RdsReadResult<IReadOnlyList<RdsWorkSiteSnapshot>>> GetWorkSitesAsync(
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        using var document = await ReadAsync(WorkSitesEndpoint, new { }, correlationId, cancellationToken);
        var root = document.RootElement;
        var (code, message, data) = ValidateEnvelope(root, WorkSitesEndpoint, correlationId);
        if (data.ValueKind != JsonValueKind.Array)
        {
            throw Failure("RDS_INVALID_RESPONSE", "RDS WorkSite data is not an array.", WorkSitesEndpoint, correlationId, code);
        }

        var values = new List<RdsWorkSiteSnapshot>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw Failure("RDS_INVALID_RESPONSE", "RDS WorkSite item is not an object.", WorkSitesEndpoint, correlationId, code);
            }

            var siteId = Text(item, "siteId")?.Trim();
            if (string.IsNullOrEmpty(siteId))
            {
                throw Failure("RDS_INVALID_RESPONSE", "RDS WorkSite item has no siteId.", WorkSitesEndpoint, correlationId, code);
            }

            values.Add(new RdsWorkSiteSnapshot(
                siteId,
                Text(item, "area"),
                Text(item, "groupName"),
                Number(item, "type"),
                Number(item, "filled"),
                Number(item, "locked"),
                Text(item, "lockedBy"),
                Number(item, "disabled"),
                Number(item, "syncFailed"),
                Text(item, "content", preserveEmpty: true),
                Text(item, "agvId"),
                item.Clone()));
        }

        var readAt = MarkSuccess();
        logger.LogInformation("RDS read succeeded for {Endpoint} with {Count} records and correlation {CorrelationId}.", WorkSitesEndpoint, values.Count, correlationId);
        return new(values, code, message, WorkSitesEndpoint, correlationId, readAt);
    }

    public async Task<RdsReadResult<RdsWindTaskPage>> QueryWindTasksAsync(
        RdsWindTaskQuery query,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.CurrentPage < 1 || query.PageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "RDS task query page values are outside the supported range.");
        }

        var filters = new Dictionary<string, object?>();
        Add(filters, "taskRecordId", query.TaskRecordId);
        Add(filters, "taskLabel", query.TaskLabel);
        Add(filters, "status", query.Status);
        Add(filters, "agvId", query.AgvId);
        var body = new { query.CurrentPage, query.PageSize, queryParam = filters };
        using var document = await ReadAsync(WindTasksEndpoint, body, correlationId, cancellationToken);
        var root = document.RootElement;
        var (code, message, data) = ValidateEnvelope(root, WindTasksEndpoint, correlationId);
        if (data.ValueKind != JsonValueKind.Object)
        {
            throw Failure("RDS_INVALID_RESPONSE", "RDS Wind Task data is not an object.", WindTasksEndpoint, correlationId, code);
        }

        var tasks = new List<RdsWindTaskSnapshot>();
        if (data.TryGetProperty("pageList", out var pageList) && pageList.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in pageList.EnumerateArray())
            {
                tasks.Add(new RdsWindTaskSnapshot(
                    Text(item, "taskRecordId"),
                    Text(item, "taskLabel"),
                    Number(item, "status"),
                    Text(item, "agvId"),
                    Text(item, "outOrderNo"),
                    item.Clone()));
            }
        }
        else if (data.TryGetProperty("pageList", out pageList) && pageList.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            throw Failure("RDS_INVALID_RESPONSE", "RDS Wind Task pageList is not an array or null.", WindTasksEndpoint, correlationId, code);
        }

        var page = new RdsWindTaskPage(
            Number(data, "totalCount") ?? tasks.Count,
            Number(data, "currentPage") ?? query.CurrentPage,
            Number(data, "pageSize") ?? query.PageSize,
            Number(data, "totalPage") ?? 0,
            tasks);
        var readAt = MarkSuccess();
        logger.LogInformation("RDS read succeeded for {Endpoint} with {Count} records and correlation {CorrelationId}.", WindTasksEndpoint, tasks.Count, correlationId);
        return new(page, code, message, WindTasksEndpoint, correlationId, readAt);
    }

    private async Task<JsonDocument> ReadAsync(string endpoint, object body, Guid correlationId, CancellationToken cancellationToken)
    {
        if (!Enabled || options.BaseAddress is null)
        {
            throw Failure("RDS_DISABLED", "RDS read integration is disabled.", endpoint, correlationId);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.TryAddWithoutValidation("language", "en");
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId.ToString());
            using var response = await httpClientFactory.CreateClient("RdsReadOnly").SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw Failure("RDS_HTTP_FAILURE", $"RDS returned HTTP {(int)response.StatusCode}.", endpoint, correlationId);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            try
            {
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (JsonException error)
            {
                throw Failure("RDS_INVALID_JSON", "RDS returned invalid JSON.", endpoint, correlationId, innerException: error);
            }
        }
        catch (RdsReadException)
        {
            throw;
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure("RDS_TIMEOUT", "RDS read timed out.", endpoint, correlationId, innerException: error);
        }
        catch (HttpRequestException error)
        {
            throw Failure("RDS_UNAVAILABLE", "RDS read endpoint is unavailable.", endpoint, correlationId, innerException: error);
        }
    }

    private static (int Code, string Message, JsonElement Data) ValidateEnvelope(JsonElement root, string endpoint, Guid correlationId)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("code", out var codeElement)
            || !TryNumber(codeElement, out var code)
            || !root.TryGetProperty("msg", out var messageElement))
        {
            throw Failure("RDS_INVALID_RESPONSE", "RDS response does not contain a valid code/msg envelope.", endpoint, correlationId);
        }

        var message = messageElement.ValueKind == JsonValueKind.String ? messageElement.GetString() ?? string.Empty : messageElement.ToString();
        if (code != 200)
        {
            throw Failure("RDS_BUSINESS_FAILURE", $"RDS business request failed: {message}", endpoint, correlationId, code);
        }

        if (!root.TryGetProperty("data", out var data))
        {
            throw Failure("RDS_INVALID_RESPONSE", "RDS response contains no data field.", endpoint, correlationId, code);
        }

        return (code, message, data);
    }

    private DateTimeOffset MarkSuccess()
    {
        var value = timeProvider.GetUtcNow();
        lock (lastReadLock) lastSuccessfulRead = value;
        return value;
    }

    private static string? Text(JsonElement item, string name, bool preserveEmpty = false)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        return preserveEmpty || !string.IsNullOrWhiteSpace(text) ? text : null;
    }

    private static int? Number(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && TryNumber(value, out var number) ? number : null;

    private static bool TryNumber(JsonElement value, out int number) =>
        value.ValueKind == JsonValueKind.Number
            ? value.TryGetInt32(out number)
            : int.TryParse(value.ValueKind == JsonValueKind.String ? value.GetString() : null, out number);

    private static void Add(Dictionary<string, object?> target, string key, object? value)
    {
        if (value is string text ? !string.IsNullOrWhiteSpace(text) : value is not null)
        {
            target[key] = value is string normalized ? normalized.Trim() : value;
        }
    }

    private static RdsReadException Failure(string code, string message, string endpoint, Guid correlationId, int? businessCode = null, Exception? innerException = null) =>
        new(code, message, endpoint, correlationId, businessCode, innerException);
}
