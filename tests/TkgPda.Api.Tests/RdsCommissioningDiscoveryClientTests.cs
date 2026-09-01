using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TkgPda.Application;
using Xunit;

namespace TkgPda.Api.Tests;

public sealed class RdsCommissioningDiscoveryClientTests
{
    [Fact]
    public async Task Default_inventory_parses_documented_core_shapes_without_business_state_inference()
    {
        var handler = new RoutingHandler(DefaultResponse);
        var snapshot = await Client(handler).CaptureAsync(
            new(),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            DateTimeOffset.Parse("2026-09-02T10:00:00Z"));

        var robot = Assert.Single(snapshot.Robots);
        Assert.Equal("robot-uuid", robot.CanonicalId);
        Assert.Equal("robot-uuid", robot.Uuid);
        Assert.Equal("sim_01", robot.VehicleId);
        Assert.Equal(["AP15", "AP20"], robot.ParkPoints);
        Assert.Single(robot.Alarms);

        var workSite = Assert.Single(snapshot.WorkSites);
        Assert.Equal("BIN-01", workSite.Id);
        Assert.Equal("AP15", workSite.PointName);
        Assert.Equal("1", workSite.Filled?.GetRawText());
        Assert.Equal("\"KLT\"", workSite.Container?.GetRawText());
        Assert.Equal("\"G-1\"", workSite.GoodsId?.GetRawText());
        Assert.Equal("true", workSite.Loaded?.GetRawText());
        var task = Assert.Single(workSite.BinTasks);
        Assert.Equal("load", task.Key);
        Assert.Equal("Script", task.Operation);
        Assert.Equal("safe-script", task.ScriptName);
        Assert.Contains("[REDACTED]", task.SafeArguments?.GetRawText(), StringComparison.Ordinal);

        Assert.Equal(2, snapshot.Devices.Count);
        Assert.Equal("0.500000", Assert.Single(snapshot.ChargeParameters).ChargeNeed);
        Assert.Equal(["LM1"], snapshot.Navigation.DisabledPoints);
        Assert.Equal(["LM1-LM2"], snapshot.Navigation.DisabledPaths);
        Assert.Equal("NOT_CAPTURED", snapshot.SelectedCoreParameters.Status);
        Assert.DoesNotContain(handler.Requests, item => item.Path == "/getCoreParam");
        Assert.Equal(7, handler.Requests.Count);
        Assert.Equal("{\"bins\":[]}", handler.Requests.Single(item => item.Path == "/binCheck").Body);
    }

    [Fact]
    public async Task Http_200_with_nonzero_core_code_is_business_failure()
    {
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath == "/devicesDetails"
            ? Json("{\"code\":50002,\"msg\":\"unsupported\"}")
            : DefaultResponse(request));

        var snapshot = await Client(handler).CaptureAsync(new(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var result = snapshot.EndpointResults.Single(item => item.Operation == "DevicesDetails");

        Assert.Equal("FAILED", result.Status);
        Assert.Equal(50002, result.BusinessCode);
        Assert.Equal("RDSCORE_BUSINESS_FAILURE", result.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "NOT_FOUND", "RDSCORE_NOT_FOUND")]
    [InlineData(HttpStatusCode.BadGateway, "FAILED", "RDSCORE_HTTP_FAILURE")]
    public async Task Http_failures_are_captured_without_aborting_inventory(HttpStatusCode status, string expectedStatus, string expectedCode)
    {
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath == "/getChargeParam"
            ? Json("{\"code\":50000,\"msg\":\"failure\"}", status)
            : DefaultResponse(request));

        var snapshot = await Client(handler).CaptureAsync(new(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var result = snapshot.EndpointResults.Single(item => item.Operation == "ChargeParameters");

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Equal((int)status, result.HttpStatus);
    }

    [Theory]
    [InlineData(FailureKind.Timeout, "RDSCORE_TIMEOUT")]
    [InlineData(FailureKind.Connection, "RDSCORE_UNAVAILABLE")]
    [InlineData(FailureKind.MalformedJson, "RDSCORE_INVALID_JSON")]
    public async Task Transport_and_json_failures_are_isolated(FailureKind kind, string expectedCode)
    {
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath != "/getDisablePoints") return DefaultResponse(request);
            return kind switch
            {
                FailureKind.Timeout => throw new TaskCanceledException("mock timeout"),
                FailureKind.Connection => throw new HttpRequestException("mock connection failure"),
                _ => Json("not-json")
            };
        });

        var snapshot = await Client(handler).CaptureAsync(new(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var result = snapshot.EndpointResults.Single(item => item.Operation == "DisabledPoints");

        Assert.Equal("FAILED", result.Status);
        Assert.Equal(expectedCode, result.ErrorCode);
    }

    [Fact]
    public async Task Optional_fields_and_unknown_future_fields_are_tolerated()
    {
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath == "/robotsStatus"
            ? Json("{\"report\":[{\"vehicle_id\":\"legacy-id\",\"future\":{\"anything\":true}}],\"newTopLevel\":7}")
            : DefaultResponse(request));

        var snapshot = await Client(handler).CaptureAsync(new(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var robot = Assert.Single(snapshot.Robots);

        Assert.Equal("legacy-id", robot.CanonicalId);
        Assert.Null(robot.Uuid);
        Assert.Equal("legacy-id", robot.VehicleId);
    }

    [Fact]
    public void Registry_is_closed_read_discovery_only_and_has_no_arbitrary_request_surface()
    {
        var descriptors = RdsCoreDiscoveryClient.AllowedOperations;
        Assert.Equal(13, descriptors.Count);
        Assert.Equal(
            [
                "/robotsStatus", "/binCheck", "/devicesDetails", "/getRobotParkPoints",
                "/getChargeParam", "/getDisablePoints", "/getDisablePaths", "/getCoreParam",
                "/queryConnectivity", "/queryDist", "/orderDetails/{id}",
                "/orderDetailsByExternalId/{externalId}", "/blockDetailsById/{blockId}"
            ],
            descriptors.Select(item => item.Endpoint).ToArray());
        var forbidden = new[] { "/setOrder", "/terminate", "/setForkHeight", "/controlMotion", "/setRobotIO", "/setCoreParam" };
        Assert.DoesNotContain(descriptors, item => forbidden.Contains(item.Endpoint, StringComparer.Ordinal));

        var publicMethods = typeof(RdsCoreDiscoveryClient).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.DoesNotContain(publicMethods.SelectMany(method => method.GetParameters()), parameter => parameter.ParameterType == typeof(HttpMethod));
        Assert.DoesNotContain(publicMethods, method => method.Name.Contains("Request", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Explicit_typed_probes_use_only_registered_endpoints_and_sanitize_results()
    {
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/queryConnectivity" => Json("{\"AP1\":1,\"future\":true}"),
            "/queryDist" => Json("{\"robot-uuid\":{\"AP1\":12.5}}"),
            "/orderDetails/order-1" => Json("{\"id\":\"order-1\",\"token\":\"sensitive\"}"),
            "/orderDetailsByExternalId/ext-1" => Json("{\"externalId\":\"ext-1\"}"),
            "/blockDetailsById/block-1" => Json("{\"id\":\"block-1\"}"),
            _ => DefaultResponse(request)
        });
        var command = new RdsCoreCaptureCommand(
            [new("g2", ["AP1"])],
            [new("robot-uuid", ["AP1"])],
            ["order-1"],
            ["ext-1"],
            ["block-1"]);

        var snapshot = await Client(handler).CaptureAsync(command, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Single(snapshot.Navigation.Connectivity);
        Assert.Single(snapshot.Navigation.Distances);
        Assert.Equal(2, snapshot.OrderProbes.Count);
        Assert.Single(snapshot.BlockProbes);
        Assert.Contains("[REDACTED]", snapshot.OrderProbes.Single(item => item.Key == "order-1").SafeResult?.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(handler.Requests, item => item.Method == HttpMethod.Post && item.Path == "/queryConnectivity");
        Assert.Contains(handler.Requests, item => item.Method == HttpMethod.Post && item.Path == "/queryDist");
        Assert.Contains(handler.Requests, item => item.Method == HttpMethod.Get && item.Path == "/orderDetails/order-1");
        Assert.Contains(handler.Requests, item => item.Method == HttpMethod.Get && item.Path == "/orderDetailsByExternalId/ext-1");
        Assert.Contains(handler.Requests, item => item.Method == HttpMethod.Get && item.Path == "/blockDetailsById/block-1");
    }

    [Fact]
    public async Task Disabled_discovery_fails_closed_with_zero_http_calls()
    {
        var handler = new RoutingHandler(DefaultResponse);
        var client = new RdsCoreDiscoveryClient(
            new Factory(handler),
            new(false, null, 5),
            NullLogger<RdsCoreDiscoveryClient>.Instance);

        var error = await Assert.ThrowsAsync<RdsCommissioningDiscoveryException>(() =>
            client.CaptureAsync(new(), Guid.NewGuid(), DateTimeOffset.UtcNow));

        Assert.Equal("RDSCORE_DISCOVERY_DISABLED", error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Base_url_is_independent_strict_and_diagnostic_origin_is_sanitized()
    {
        Assert.Throws<InvalidOperationException>(() => new RdsCoreDiscoveryOptions(true, null, 5).Validate());
        Assert.Throws<InvalidOperationException>(() => new RdsCoreDiscoveryOptions(true, "http://user:secret@host:8088/", 5, true).Validate());
        Assert.Throws<InvalidOperationException>(() => new RdsCoreDiscoveryOptions(true, "http://host:8088/private", 5, true).Validate());
        var client = Client(new RoutingHandler(DefaultResponse), "https://rds.example.test:8088/");
        Assert.Equal("https://rds.example.test:8088", client.SafeOrigin);
    }

    private static RdsCoreDiscoveryClient Client(RoutingHandler handler, string baseUrl = "http://127.0.0.1:18088/")
    {
        var options = new RdsCoreDiscoveryOptions(true, baseUrl, 5, true);
        options.Validate();
        return new(new Factory(handler, options.BaseAddress), options, NullLogger<RdsCoreDiscoveryClient>.Instance);
    }

    private static HttpResponseMessage DefaultResponse(HttpRequestMessage request) => request.RequestUri!.AbsolutePath switch
    {
        "/robotsStatus" => Json("""
            {"code":0,"scene_md5":"scene","model_md5":"model","unknown":"future","report":[
              {"uuid":"robot-uuid","vehicle_id":"sim_01","dispatchable":true,"labels":["Simulation"],
               "basic_info":{"current_group":"g2"},"rbk_report":{"current_map":"map-a","battery_level":0.8,
               "errors":[{"code":"E1","desc":"example","times":1,"timestamp":123}]}}
            ]}
            """),
        "/binCheck" => Json("""
            {"bins":[{"id":"BIN-01","exist":true,"valid":true,"status":{"point_name":"AP15","filled":1,
              "container":"KLT","goodsId":"G-1","loaded":true,"binTask":[{"load":{"operation":"Script",
              "script_name":"safe-script","password":"must-redact","args":{"height":1}}}]}}]}
            """),
        "/devicesDetails" => Json("""
            {"code":0,"msg":"ok","terminals":[{"name":"T1","available":true,"connection_status":true,"future":1}],
             "lifts":[{"name":"L1","available":false,"connection_status":0}]}
            """),
        "/getRobotParkPoints" => Json("[{\"vehicle\":\"sim_01\",\"parkPoints\":[\"AP20\",\"AP15\"],\"currentTarget\":\"AP15\"}]"),
        "/getChargeParam" => Json("{\"sim_01\":{\"chargeNeed\":\"0.500000\",\"chargeOnly\":\"0.200000\",\"chargedFull\":\"0.900000\",\"chargedOK\":\"0.700000\"}}"),
        "/getDisablePoints" => Json("[{\"id\":\"LM1\"}]"),
        "/getDisablePaths" => Json("[{\"id\":\"LM1-LM2\"}]"),
        _ => Json("{\"code\":0,\"msg\":\"ok\"}")
    };

    private static HttpResponseMessage Json(string value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(value) };

    public enum FailureKind { Timeout, Connection, MalformedJson }

    private sealed class Factory(HttpMessageHandler handler, Uri? baseAddress = null) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = baseAddress };
    }

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        public List<ObservedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return route(request);
        }
    }

    private sealed record ObservedRequest(HttpMethod Method, string Path, string Body);
}
