using System.Net;
using System.Text.Json;
using TkgPda.Application;
using Xunit;

namespace TkgPda.Api.Tests;

public sealed class RdsExecutionClientTests
{
    [Fact]
    public void Dispatch_refuses_insecure_remote_http_and_short_secrets()
    {
        var remote = new RdsOptions(true, "http://10.10.10.10:8080", 5, true, new string('i', 40), "http://127.0.0.1/callback", new string('c', 40));
        Assert.Throws<InvalidOperationException>(remote.Validate);

        var shortSecret = new RdsOptions(true, "http://127.0.0.1:8080", 5, true, "short", "http://127.0.0.1/callback", "short");
        Assert.Throws<InvalidOperationException>(shortSecret.Validate);

        var remoteCallback = new RdsOptions(true, "http://127.0.0.1:8080", 5, true, new string('i', 40), "http://10.10.10.11/callback", new string('c', 40));
        Assert.Throws<InvalidOperationException>(remoteCallback.Validate);
    }

    [Fact]
    public async Task Stop_task_sends_release_site_and_immutable_record_identity()
    {
        var handler = new RecordingHandler("""{"code":200,"msg":"Success","data":null}""");
        var client = Client(handler);
        var recordId = Guid.NewGuid().ToString();

        var result = await client.StopTaskAsync(recordId, Guid.NewGuid());

        Assert.Equal(200, result.Code);
        Assert.Equal("/api/stop-task", handler.LastPath);
        using var payload = JsonDocument.Parse(handler.LastBody);
        Assert.Equal(1, payload.RootElement.GetProperty("releaseSite").GetInt32());
        Assert.Equal(recordId, payload.RootElement.GetProperty("taskRecordId").GetString());
    }

    [Fact]
    public async Task Script_response_can_be_direct_or_wrapped_json_without_losing_replay_proof()
    {
        var inner = "{\"code\":200,\"message\":\"IDEMPOTENT_REPLAY\",\"data\":{\"idempotentReplay\":true}}";
        var handler = new RecordingHandler(JsonSerializer.Serialize(new { code = 200, msg = "Success", data = inner }));
        var client = Client(handler);

        var result = await client.CreateTransportAsync(
            new(Guid.NewGuid().ToString(), "SIM_TASK", 25, new Dictionary<string, object?> { ["fromSite"] = "A" }),
            Guid.NewGuid());

        Assert.True(result.Accepted);
        Assert.True(result.IdempotentReplay);
        Assert.DoesNotContain(new string('i', 40), result.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // Global mutation policy applied to the Wind engine.
    // Stopping an existing WindTask reduces motion, exactly like the native
    // /terminate path, so it must stay reachable when the global dispatch gate is
    // closed. Creating work must not.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Wind_cancellation_stays_available_when_the_global_dispatch_gate_is_closed()
    {
        var handler = new RecordingHandler("""{"code":200,"msg":"Success","data":null}""");
        var client = GatedClient(handler, dispatchEnabled: false);

        var result = await client.StopTaskAsync(Guid.NewGuid().ToString(), Guid.NewGuid());

        Assert.Equal(200, result.Code);
        Assert.Equal("/api/stop-task", handler.LastPath);
    }

    [Fact]
    public async Task Wind_task_creation_is_blocked_when_the_global_dispatch_gate_is_closed()
    {
        var handler = new RecordingHandler("""{"code":200,"msg":"Success","data":null}""");
        var client = GatedClient(handler, dispatchEnabled: false);

        var error = await Assert.ThrowsAsync<RdsExecutionException>(() => client.CreateTransportAsync(
            new RdsCreateTransportRequest("rec-1", "TKG_TRANSPORT", 100, new Dictionary<string, object?>()),
            Guid.NewGuid()));

        Assert.Equal("RDS_DISPATCH_DISABLED", error.Code);
        Assert.Null(handler.LastPath);
    }

    [Fact]
    public async Task Wind_content_update_is_blocked_when_the_global_dispatch_gate_is_closed()
    {
        var handler = new RecordingHandler("""{"code":200,"msg":"Success","data":null}""");
        var client = GatedClient(handler, dispatchEnabled: false);

        var error = await Assert.ThrowsAsync<RdsExecutionException>(
            () => client.UpdateContentAsync("SITE-1", "EMPTY", Guid.NewGuid()));

        Assert.Equal("RDS_DISPATCH_DISABLED", error.Code);
        Assert.Null(handler.LastPath);
    }

    [Fact]
    public async Task Wind_cancellation_is_also_available_with_the_gate_open()
    {
        var handler = new RecordingHandler("""{"code":200,"msg":"Success","data":null}""");
        var client = GatedClient(handler, dispatchEnabled: true);

        var result = await client.StopTaskAsync(Guid.NewGuid().ToString(), Guid.NewGuid());

        Assert.Equal(200, result.Code);
    }

    private static RdsExecutionClient GatedClient(RecordingHandler handler, bool dispatchEnabled)
    {
        var options = dispatchEnabled
            ? new RdsOptions(true, "http://127.0.0.1:8080", 5, true, new string('i', 40), "http://127.0.0.1/callback", new string('c', 40))
            : new RdsOptions(true, "http://127.0.0.1:8080", 5, false);
        options.Validate();
        return new RdsExecutionClient(new Factory(handler, options.BaseAddress), options);
    }

    private static RdsExecutionClient Client(RecordingHandler handler)
    {
        var options = new RdsOptions(
            true,
            "http://127.0.0.1:8080",
            5,
            true,
            new string('i', 40),
            "http://127.0.0.1/callback",
            new string('c', 40));
        options.Validate();
        return new RdsExecutionClient(new Factory(handler, options.BaseAddress), options);
    }

    private sealed class Factory(HttpMessageHandler handler, Uri? baseAddress) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = baseAddress };
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public string? LastPath { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri?.AbsolutePath;
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseJson) };
        }
    }
}
