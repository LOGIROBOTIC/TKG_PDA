using System.Net;
using Xunit;

namespace TkgPda.Api.Tests;

public sealed class OperatorWebCorsTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public async Task Configured_operator_origin_receives_only_the_required_cors_contract()
    {
        using var client = fixture.Client();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        request.Headers.Add("Origin", "https://operator.example.test");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add(
            "Access-Control-Request-Headers",
            "authorization,content-type,x-device-code,idempotency-key,x-correlation-id");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            "https://operator.example.test",
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        var methods = Assert.Single(response.Headers.GetValues("Access-Control-Allow-Methods"));
        Assert.Contains("GET", methods, StringComparison.Ordinal);
        Assert.Contains("POST", methods, StringComparison.Ordinal);
        Assert.Contains("PUT", methods, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE", methods, StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Unconfigured_origin_receives_no_cors_authorization()
    {
        using var client = fixture.Client();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        request.Headers.Add("Origin", "https://untrusted.example.test");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        using var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }
}
