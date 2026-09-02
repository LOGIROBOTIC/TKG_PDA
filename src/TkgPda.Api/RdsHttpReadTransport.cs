using System.Text.Json;

namespace TkgPda.Api;

internal sealed record RdsHttpJsonResponse(int StatusCode, JsonDocument? Document) : IDisposable
{
    public void Dispose() => Document?.Dispose();
}

internal static class RdsHttpReadTransport
{
    public static async Task<RdsHttpJsonResponse> SendAsync(
        IHttpClientFactory httpClientFactory,
        string clientName,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClientFactory.CreateClient(clientName).SendAsync(
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
}
