using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using TkgPda.Application;
using Xunit;

namespace TkgPda.Api.Tests;

public sealed class RdsReadClientTests
{
    [Fact]
    public async Task Worksite_response_preserves_external_evidence()
    {
        var handler = new RecordingHandler("""
            {"code":200,"msg":"Success","data":[{"siteId":"SITE-1","type":1,"filled":1,"locked":0,"disabled":1,"syncFailed":0,"content":" exact content ","area":"A","groupName":"G","agvId":"AGV-1"}]}
            """);
        var client = Client(handler);

        var result = await client.GetWorkSitesAsync(Guid.NewGuid());
        var site = Assert.Single(result.Data);

        Assert.Equal(200, result.BusinessCode);
        Assert.Equal("SITE-1", site.SiteId);
        Assert.Equal(1, site.Type);
        Assert.Equal(1, site.Filled);
        Assert.Equal(0, site.Locked);
        Assert.Equal(1, site.Disabled);
        Assert.Equal(0, site.SyncFailed);
        Assert.Equal(" exact content ", site.Content);
        Assert.Contains("\"siteId\":\"SITE-1\"", site.RawState.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("/api/work-sites/sites", handler.LastPath);
        Assert.Equal("en", handler.LastLanguage);
    }

    [Fact]
    public async Task Http_200_business_failure_is_rejected_with_context()
    {
        var client = Client(new RecordingHandler("{\"code\":60008,\"msg\":\"Failure\",\"data\":null}"));

        var error = await Assert.ThrowsAsync<RdsReadException>(() => client.GetWorkSitesAsync(Guid.Parse("11111111-1111-1111-1111-111111111111")));

        Assert.Equal("RDS_BUSINESS_FAILURE", error.Code);
        Assert.Equal(60008, error.BusinessCode);
        Assert.Equal("/api/work-sites/sites", error.Endpoint);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), error.CorrelationId);
    }

    [Fact]
    public async Task Empty_wind_task_page_is_a_valid_read_result()
    {
        var handler = new RecordingHandler("""
            {"code":200,"msg":"Success","data":{"totalCount":0,"currentPage":1,"pageSize":5,"totalPage":0,"pageList":null}}
            """);
        var client = Client(handler);

        var result = await client.QueryWindTasksAsync(new RdsWindTaskQuery(PageSize: 5), Guid.NewGuid());

        Assert.Equal(0, result.Data.TotalCount);
        Assert.Empty(result.Data.Tasks);
        Assert.Equal("/api/queryWindTask", handler.LastPath);
        Assert.Contains("\"pageSize\":5", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disabled_client_does_not_send_http()
    {
        var handler = new RecordingHandler("{}");
        var options = new RdsOptions(false, null, 5);
        var client = new RdsReadClient(new Factory(handler), options, TimeProvider.System, NullLogger<RdsReadClient>.Instance);

        var error = await Assert.ThrowsAsync<RdsReadException>(() => client.GetWorkSitesAsync(Guid.NewGuid()));

        Assert.Equal("RDS_DISABLED", error.Code);
        Assert.Equal(0, handler.Calls);
    }

    private static RdsReadClient Client(RecordingHandler handler)
    {
        var options = new RdsOptions(true, "http://127.0.0.1:8080/private/path?hidden=yes", 5);
        return new RdsReadClient(new Factory(handler, options.BaseAddress), options, TimeProvider.System, NullLogger<RdsReadClient>.Instance);
    }

    private sealed class Factory(HttpMessageHandler handler, Uri? baseAddress = null) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = baseAddress };
    }

    private sealed class RecordingHandler(string json, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastPath { get; private set; }
        public string? LastLanguage { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastPath = request.RequestUri?.AbsolutePath;
            LastLanguage = request.Headers.TryGetValues("language", out var values) ? values.Single() : null;
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode) { Content = new StringContent(json) };
        }
    }
}
