using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TkgPda.Application;
using Xunit;

namespace TkgPda.Api.Tests;

public sealed class RdsCoreNativeOrderClientTests
{
    private static readonly Guid RequestId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public async Task Navigation_order_serializes_all_supported_fields_with_sealed_deterministic_contract()
    {
        var handler = new RecordingHandler(_ => Json("""{"code":0,"msg":"ok","order_details":{"errors":[],"warnings":[],"notices":[]},"future":{"x":1}}"""));
        var client = Client(handler);
        var order = RdsCoreNormalOrder.Create(
            RequestId,
            Navigation("pickup", "SIM-A"),
            Navigation("dropoff", "SIM-B"),
            externalId: "business-1",
            vehicle: "sim_01",
            group: "g2",
            label: "commissioning",
            keyRoute: ["route-key", "route-key-2"],
            keyTask: "task-key",
            priority: 10);

        var result = await client.SetOrderAsync(order, Guid.NewGuid());

        Assert.True(result.HttpAccepted);
        Assert.True(result.CoreAccepted);
        Assert.True(result.ExecutionValidated);
        Assert.True(result.RawResponse!.Value.GetProperty("future").GetProperty("x").GetInt32() == 1);
        var payload = JsonDocument.Parse(handler.Single().Body).RootElement;
        Assert.Equal(RequestId.ToString("D"), payload.GetProperty("id").GetString());
        Assert.Equal("business-1", payload.GetProperty("externalId").GetString());
        Assert.Equal("sim_01", payload.GetProperty("vehicle").GetString());
        Assert.Equal("g2", payload.GetProperty("group").GetString());
        Assert.Equal("commissioning", payload.GetProperty("label").GetString());
        Assert.Equal(new string?[] { "route-key", "route-key-2" }, payload.GetProperty("keyRoute").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal("task-key", payload.GetProperty("keyTask").GetString());
        Assert.Equal(10, payload.GetProperty("priority").GetInt32());
        Assert.True(payload.GetProperty("complete").GetBoolean());
        var blocks = payload.GetProperty("blocks");
        Assert.Equal($"{RequestId:D}:pickup", blocks[0].GetProperty("blockId").GetString());
        Assert.Equal($"{RequestId:D}:dropoff", blocks[1].GetProperty("blockId").GetString());
        Assert.False(blocks[0].TryGetProperty("operation", out _));
    }

    [Fact]
    public async Task Operation_script_and_binTask_shapes_are_serialized_without_invented_fields()
    {
        var handler = new RecordingHandler(_ => Json("""{"code":0,"order_details":{"errors":[]}}"""));
        var client = Client(handler);
        var args = Element("""{"height":1}""");
        var post = Element("""{"wait":true}""");
        var orders = new[]
        {
            RdsCoreNormalOrder.Create(RequestId,
                new RdsCoreOperationBlock(Block("pickup"), "A", "ExplicitOperation", args, post), Navigation("dropoff", "B")),
            RdsCoreNormalOrder.Create(RequestId,
                new RdsCoreScriptBlock(Block("pickup"), "A", "ExplicitScript", args, post), Navigation("dropoff", "B")),
            RdsCoreNormalOrder.Create(RequestId,
                new RdsCoreBinTaskBlock(Block("pickup"), "A", "ExplicitBinTask", "goods-1", post), Navigation("dropoff", "B"))
        };

        foreach (var order in orders) await client.SetOrderAsync(order, Guid.NewGuid());

        var operation = JsonDocument.Parse(handler.Requests[0].Body).RootElement.GetProperty("blocks")[0];
        Assert.Equal("ExplicitOperation", operation.GetProperty("operation").GetString());
        Assert.Equal(1, operation.GetProperty("operationArgs").GetProperty("height").GetInt32());
        var script = JsonDocument.Parse(handler.Requests[1].Body).RootElement.GetProperty("blocks")[0];
        Assert.Equal("Script", script.GetProperty("operation").GetString());
        Assert.Equal("ExplicitScript", script.GetProperty("scriptName").GetString());
        Assert.True(script.GetProperty("postAction").GetProperty("wait").GetBoolean());
        var bin = JsonDocument.Parse(handler.Requests[2].Body).RootElement.GetProperty("blocks")[0];
        Assert.Equal("ExplicitBinTask", bin.GetProperty("binTask").GetString());
        Assert.Equal("goods-1", bin.GetProperty("goodsId").GetString());
        Assert.False(bin.TryGetProperty("operation", out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, "{\"code\":0}", false, false, false, false)]
    [InlineData(HttpStatusCode.OK, "{\"code\":0}", true, true, false, false)]
    [InlineData(HttpStatusCode.OK, "{\"code\":42,\"msg\":\"no\"}", true, false, false, false)]
    [InlineData(HttpStatusCode.BadRequest, "{\"code\":50003,\"msg\":\"duplicate\"}", false, false, false, true)]
    [InlineData(HttpStatusCode.OK, "{\"code\":0,\"order_details\":{\"errors\":[{\"code\":\"E\"}]}}", true, true, false, false)]
    public async Task SetOrder_distinguishes_http_core_execution_and_duplicate_semantics(
        HttpStatusCode status,
        string body,
        bool http,
        bool core,
        bool execution,
        bool duplicate)
    {
        var client = Client(new RecordingHandler(_ => Json(body, status)));

        var result = await client.SetOrderAsync(NavigationOrder(), Guid.NewGuid());

        Assert.Equal(http, result.HttpAccepted);
        Assert.Equal(core, result.CoreAccepted);
        Assert.Equal(execution, result.ExecutionValidated);
        Assert.Equal(duplicate, result.DuplicateOrderId);
    }

    [Fact]
    public async Task SetOrder_preserves_errors_warnings_and_notices()
    {
        var client = Client(new RecordingHandler(_ => Json("""
            {"code":0,"order_details":{"errors":[],"warnings":[{"code":"W1"}],"notices":["N1"]}}
            """)));

        var result = await client.SetOrderAsync(NavigationOrder(), Guid.NewGuid());

        Assert.True(result.ExecutionValidated);
        Assert.Empty(result.Diagnostics.Errors);
        Assert.Equal("W1", result.Diagnostics.Warnings[0].Value.GetProperty("code").GetString());
        Assert.Equal("N1", result.Diagnostics.Notices[0].Value.GetString());
    }

    [Fact]
    public async Task Successful_malformed_JSON_is_rejected()
    {
        var client = Client(new RecordingHandler(_ => Json("not-json")));

        var error = await Assert.ThrowsAsync<RdsCoreNativeOrderException>(() =>
            client.SetOrderAsync(NavigationOrder(), Guid.NewGuid()));

        Assert.Equal("RDSCORE_INVALID_JSON", error.Code);
    }

    public static TheoryData<string, RdsCoreKnownOrderState> OrderStates => new()
    {
        { "CREATED", RdsCoreKnownOrderState.Created },
        { "WAITING", RdsCoreKnownOrderState.Waiting },
        { "RUNNING", RdsCoreKnownOrderState.Running },
        { "SUSPENDED", RdsCoreKnownOrderState.Suspended },
        { "FINISHED", RdsCoreKnownOrderState.Finished },
        { "FAILED", RdsCoreKnownOrderState.Failed },
        { "STOPPED", RdsCoreKnownOrderState.Stopped },
        { "Error", RdsCoreKnownOrderState.Error },
        { "TOBEDISPATCHED", RdsCoreKnownOrderState.ToBeDispatched },
        { "FUTURE_STATE", RdsCoreKnownOrderState.Unknown }
    };

    [Theory]
    [MemberData(nameof(OrderStates))]
    public async Task Order_details_parses_known_and_unknown_states_without_losing_raw(string raw, RdsCoreKnownOrderState known)
    {
        var response = JsonSerializer.Serialize(new { code = 0, data = new { id = RequestId.ToString("D"), state = raw, future = true } });
        var handler = new RecordingHandler(_ => Json(response));

        var order = await Client(handler).GetOrderDetailsAsync(RequestId.ToString("D"), Guid.NewGuid());

        Assert.Equal(known, order.State.Known);
        Assert.Equal(raw, order.State.Raw);
        Assert.True(order.Raw.GetProperty("future").GetBoolean());
        Assert.Empty(order.Blocks);
    }

    public static TheoryData<string, RdsCoreKnownBlockState> BlockStates => new()
    {
        { "CREATED", RdsCoreKnownBlockState.Created },
        { "RUNNING", RdsCoreKnownBlockState.Running },
        { "WAITING", RdsCoreKnownBlockState.Waiting },
        { "POSTING", RdsCoreKnownBlockState.Posting },
        { "SUSPENDED", RdsCoreKnownBlockState.Suspended },
        { "FINISHED", RdsCoreKnownBlockState.Finished },
        { "FAILED", RdsCoreKnownBlockState.Failed },
        { "STOPPED", RdsCoreKnownBlockState.Stopped },
        { "MANUAL_FINISHED", RdsCoreKnownBlockState.ManualFinished },
        { "Error", RdsCoreKnownBlockState.Error },
        { "FUTURE_BLOCK", RdsCoreKnownBlockState.Unknown }
    };

    [Theory]
    [MemberData(nameof(BlockStates))]
    public async Task Block_details_parses_known_unknown_and_missing_optional_fields(string raw, RdsCoreKnownBlockState known)
    {
        var response = JsonSerializer.Serialize(new { code = 0, data = new { blockId = "b1", state = raw, future = 1 } });
        var client = Client(new RecordingHandler(_ => Json(response)));

        var block = await client.GetBlockDetailsByIdAsync("b1", Guid.NewGuid());

        Assert.Equal(known, block.State.Known);
        Assert.Equal(raw, block.State.Raw);
        Assert.Null(block.Location);
        Assert.Equal(1, block.Raw.GetProperty("future").GetInt32());
    }

    [Fact]
    public async Task Order_list_parses_envelope_paging_and_unknown_fields()
    {
        var handler = new RecordingHandler(_ => Json("""
            {"list":[{"id":"o1","state":"WAITING"},{"id":"o2","state":"RUNNING","newField":7}],"total":12,"page":2,"size":5}
            """));
        var client = Client(handler);

        var result = await client.GetOrdersAsync(
            new(2, 5, RdsCoreOrderSortField.State, RdsCoreOrderSortDirection.Ascending),
            Guid.NewGuid());

        Assert.Equal(2, result.Orders.Count);
        Assert.Equal(12, result.Total);
        Assert.Equal(2, result.Page);
        Assert.Equal(5, result.PageSize);
        Assert.Equal(7, result.Orders[1].Raw.GetProperty("newField").GetInt32());
        Assert.Equal("/orders?page=2&size=5&orderBy=state&orderMethod=ascending", handler.Single().Path);
    }

    [Fact]
    public async Task Lookups_use_closed_escaped_paths_and_external_or_block_lookup_returns_arrays()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/orderDetailsByExternalId/ext%2F1" => Json("""[{"id":"a","externalId":"ext/1"},{"id":"b","externalId":"ext/1"}]"""),
            "/orderDetailsByBlockId/id%3Apickup" => Json("""{"id":"a","state":"RUNNING"}"""),
            _ => Json("""{"code":0,"data":{"id":"a","state":"WAITING"}}""")
        });
        var client = Client(handler);

        var byExternal = await client.GetOrderDetailsByExternalIdAsync("ext/1", Guid.NewGuid());
        var byBlock = await client.GetOrderDetailsByBlockIdAsync("id:pickup", Guid.NewGuid());
        var byId = await client.GetOrderDetailsAsync("a", Guid.NewGuid());

        Assert.Equal(2, byExternal.Count);
        Assert.Equal("a", byBlock.Id);
        Assert.Equal("a", byId.Id);
        Assert.Equal(
            ["/orderDetailsByExternalId/ext%2F1", "/orderDetailsByBlockId/id%3Apickup", "/orderDetails/a"],
            handler.Requests.Select(item => item.Path).ToArray());
    }

    [Fact]
    public async Task Terminate_can_only_target_one_nonempty_order_and_never_disables_vehicle()
    {
        var handler = new RecordingHandler(_ => Json("""{"code":0,"msg":"ok"}"""));
        var client = Client(handler);

        var result = await client.TerminateAsync(RequestId.ToString("D"), Guid.NewGuid());

        Assert.True(result.CoreAccepted);
        var payload = JsonDocument.Parse(handler.Single().Body).RootElement;
        Assert.Equal(RequestId.ToString("D"), payload.GetProperty("id").GetString());
        Assert.False(payload.TryGetProperty("idList", out _));
        Assert.False(payload.TryGetProperty("vehicles", out _));
        Assert.False(payload.GetProperty("disableVehicle").GetBoolean());
        await Assert.ThrowsAsync<ArgumentException>(() => client.TerminateAsync("", Guid.NewGuid()));
        Assert.Single(handler.Requests);
        var terminateMethod = typeof(IRdsCoreNativeOrderClient).GetMethod(nameof(IRdsCoreNativeOrderClient.TerminateAsync))!;
        Assert.DoesNotContain(terminateMethod.GetParameters(), parameter => typeof(System.Collections.IEnumerable).IsAssignableFrom(parameter.ParameterType) && parameter.ParameterType != typeof(string));
    }

    [Fact]
    public async Task Disabled_or_non_native_mode_fails_closed_before_any_HTTP_call()
    {
        var handler = new RecordingHandler(_ => Json("""{"code":0}"""));
        var disabled = Client(handler, new(false, RdsExecutionMode.RdsCoreNativeOrder, "http://127.0.0.1:18088/", 5, true));
        var windTask = Client(handler, new(false, RdsExecutionMode.WindTask, "http://127.0.0.1:18088/", 5, true));

        var disabledError = await Assert.ThrowsAsync<RdsCoreNativeOrderException>(() => disabled.SetOrderAsync(NavigationOrder(), Guid.NewGuid()));
        var modeError = await Assert.ThrowsAsync<RdsCoreNativeOrderException>(() => windTask.TerminateAsync(RequestId.ToString("D"), Guid.NewGuid()));

        Assert.Equal("RDSCORE_EXECUTION_DISABLED", disabledError.Code);
        Assert.Equal("RDSCORE_EXECUTION_DISABLED", modeError.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Configuration_requires_explicit_native_enable_and_shares_only_explicit_core_origin()
    {
        var defaults = RdsCoreExecutionOptions.FromConfiguration(new ConfigurationBuilder().Build());
        Assert.False(defaults.Enabled);
        Assert.Equal(RdsExecutionMode.WindTask, defaults.Mode);
        Assert.Null(defaults.BaseAddress);

        var discoveryOnly = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RdsCore:BaseUrl"] = "http://127.0.0.1:18088/",
            ["RdsCoreDiscovery:Enabled"] = "true"
        }).Build();
        var execution = RdsCoreExecutionOptions.FromConfiguration(discoveryOnly);
        var discovery = RdsCoreDiscoveryOptions.FromConfiguration(discoveryOnly);
        Assert.True(discovery.Enabled);
        Assert.False(execution.Enabled);
        Assert.False(execution.MutationEnabled);

        var enabled = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RdsCore:BaseUrl"] = "http://127.0.0.1:18088/",
            ["RdsCoreExecution:Enabled"] = "true",
            ["RdsCoreExecution:Mode"] = "RDSCORE_NATIVE_ORDER"
        }).Build();
        Assert.True(RdsCoreExecutionOptions.FromConfiguration(enabled).MutationEnabled);
        Assert.Throws<InvalidOperationException>(() => RdsCoreExecutionOptions.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RdsCoreExecution:Enabled"] = "true",
                ["RdsCoreExecution:Mode"] = "RDSCORE_NATIVE_ORDER"
            }).Build()));
        Assert.Throws<InvalidOperationException>(() => RdsCoreExecutionOptions.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RdsCore:BaseUrl"] = "http://127.0.0.1:18088/",
                ["RdsCoreExecution:Enabled"] = "true",
                ["RdsCoreExecution:Mode"] = "WIND_TASK"
            }).Build()));
    }

    [Fact]
    public void Operation_registry_is_closed_and_excludes_recovery_or_arbitrary_request_surfaces()
    {
        Assert.Equal(
            [
                "/setOrder",
                "/orderDetails/{id}",
                "/orders",
                "/orderDetailsByExternalId/{externalId}",
                "/orderDetailsByBlockId/{blockId}",
                "/blockDetailsById/{blockId}",
                "/terminate",
                "/setPriority",
                "/clearRobotAllError",
                "/gotoSitePause",
                "/gotoSiteResume",
                "/manualFinished",
                "/redoFailedOrder",
                "/dispatchable",
                "/robotsStatus",
                "/lock",
                "/unlock",
                "/switchMap"
            ],
            RdsCoreNativeOrderClient.AllowedOperations.Select(item => item.Endpoint).ToArray());
        var forbidden = new[] { "/addBlocks", "/cancelBlock", "/setLabel", "/unassignOrder", "/problemVehicle", "/setChargeOrder" };
        Assert.DoesNotContain(RdsCoreNativeOrderClient.AllowedOperations, item => forbidden.Contains(item.Endpoint, StringComparer.Ordinal));
        var publicMethods = typeof(RdsCoreNativeOrderClient).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.DoesNotContain(publicMethods.SelectMany(method => method.GetParameters()), parameter => parameter.ParameterType == typeof(HttpMethod));
    }

    [Fact]
    public async Task Set_priority_serializes_id_and_priority_to_post_setPriority()
    {
        var handler = new RecordingHandler(_ => Json("""{"code":0,"msg":"ok"}"""));
        var client = Client(handler);

        var result = await client.SetPriorityAsync("task-42", 5, Guid.NewGuid());

        Assert.True(result.HttpAccepted);
        Assert.True(result.CoreAccepted);
        Assert.Equal(0, result.Code);
        Assert.Equal("ok", result.Message);
        var req = handler.Single();
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("/setPriority", req.Path);
        var body = JsonDocument.Parse(req.Body).RootElement;
        Assert.Equal("task-42", body.GetProperty("id").GetString());
        Assert.Equal(5, body.GetProperty("priority").GetInt32());
    }

    [Fact]
    public async Task Clear_robot_all_error_serializes_vehicles_to_post_clearRobotAllError()
    {
        var handler = new RecordingHandler(_ => Json("""{"code":0,"msg":"ok"}"""));
        var client = Client(handler);

        var result = await client.ClearRobotAllErrorAsync(["Sfl-Cbd15"], Guid.NewGuid());

        Assert.True(result.CoreAccepted);
        var req = handler.Single();
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("/clearRobotAllError", req.Path);
        var body = JsonDocument.Parse(req.Body).RootElement;
        Assert.Equal("Sfl-Cbd15", body.GetProperty("vehicles")[0].GetString());
    }

    [Fact]
    public async Task Goto_site_pause_and_resume_serialize_vehicles_to_correct_endpoints()
    {
        var handler = new RecordingHandler(_ => Json("""{"code":0,"msg":"ok"}"""));
        var client = Client(handler);

        var pauseResult = await client.GotoSitePauseAsync(["Sfl-Cbd15"], Guid.NewGuid());
        Assert.True(pauseResult.CoreAccepted);
        Assert.Equal("/gotoSitePause", handler.Requests[0].Path);

        var resumeResult = await client.GotoSiteResumeAsync(["Sfl-Cbd15"], Guid.NewGuid());
        Assert.True(resumeResult.CoreAccepted);
        Assert.Equal("/gotoSiteResume", handler.Requests[1].Path);
    }

    [Fact]
    public async Task Manual_finished_and_redo_failed_order_serialize_vehicles_to_correct_endpoints()
    {
        var handler = new RecordingHandler(_ => Json("""{"code":0,"msg":"ok"}"""));
        var client = Client(handler);

        var finishResult = await client.ManualFinishedAsync(["Sfl-Cbd15"], Guid.NewGuid());
        Assert.True(finishResult.CoreAccepted);
        Assert.Equal("/manualFinished", handler.Requests[0].Path);

        var redoResult = await client.RedoFailedOrderAsync(["Sfl-Cbd15"], Guid.NewGuid());
        Assert.True(redoResult.CoreAccepted);
        Assert.Equal("/redoFailedOrder", handler.Requests[1].Path);
    }

    [Fact]
    public async Task Set_dispatchable_validates_type_and_serializes_to_post_dispatchable()
    {
        var handler = new RecordingHandler(_ => Json("""{"code":0,"msg":"ok"}"""));
        var client = Client(handler);

        var result = await client.SetDispatchableAsync(["Sfl-Cbd15"], "undispatchable_unignore", Guid.NewGuid());
        Assert.True(result.CoreAccepted);
        var req = handler.Single();
        Assert.Equal("/dispatchable", req.Path);
        var body = JsonDocument.Parse(req.Body).RootElement;
        Assert.Equal("undispatchable_unignore", body.GetProperty("type").GetString());

        await Assert.ThrowsAsync<ArgumentException>(() => client.SetDispatchableAsync(["Sfl-Cbd15"], "invalid_type", Guid.NewGuid()));
    }

    [Fact]
    public async Task Lock_and_unlock_serialize_vehicles_to_correct_endpoints()
    {
        var handler = new RecordingHandler(_ => Json("""{"code":0,"msg":"ok"}"""));
        var client = Client(handler);

        var lockResult = await client.LockAsync(["Sfl-Cbd15"], Guid.NewGuid());
        Assert.True(lockResult.CoreAccepted);
        Assert.Equal("/lock", handler.Requests[0].Path);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        var lockBody = JsonDocument.Parse(handler.Requests[0].Body).RootElement;
        Assert.Equal("Sfl-Cbd15", lockBody.GetProperty("vehicles")[0].GetString());

        var unlockResult = await client.UnlockAsync(["Sfl-Cbd15"], Guid.NewGuid());
        Assert.True(unlockResult.CoreAccepted);
        Assert.Equal("/unlock", handler.Requests[1].Path);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        var unlockBody = JsonDocument.Parse(handler.Requests[1].Body).RootElement;
        Assert.Equal("Sfl-Cbd15", unlockBody.GetProperty("vehicles")[0].GetString());
    }

    [Fact]
    public async Task Switch_map_serializes_vehicle_and_map_to_post_switchMap()
    {
        var handler = new RecordingHandler(_ => Json("""{"code":0,"msg":"ok"}"""));
        var client = Client(handler);

        var result = await client.SwitchMapAsync("Sfl-Cbd15", "26.7.2026_map_optimak_1", Guid.NewGuid());
        Assert.True(result.CoreAccepted);
        var req = handler.Single();
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("/switchMap", req.Path);
        var body = JsonDocument.Parse(req.Body).RootElement;
        Assert.Equal("Sfl-Cbd15", body.GetProperty("vehicle").GetString());
        Assert.Equal("26.7.2026_map_optimak_1", body.GetProperty("map").GetString());
    }

    // ------------------------------------------------------------------
    // Core-level alarms live on the /robotsStatus ROOT, beside "report". The parser
    // has understood them for a while, but this client only ever handed it a
    // report[] entry, so a documented root fatal reached nobody and dispatch saw a
    // healthy fleet. These tests drive the real client with full literal payloads —
    // a parser-only test cannot catch a caller that never passes the object.
    // ------------------------------------------------------------------

    private static string RootAlarmPayload(string rootAlarms, string robotAlarms = "") => $$"""
    {
      "code": 0,
      "msg": "ok",
      {{rootAlarms}}
      "report": [
        {
          "vehicle_id": "Sfl-Cbd15",
          "uuid": "Sfl-Cbd15",
          "connection_status": 1,
          "dispatchable": true,
          "is_error": false,
          "procBusiness": false,
          "basic_info": { "model": "HELIFORK", "current_map": "m1" },
          "rbk_report": { "current_station": "PP16", "battery_level": 1.0{{robotAlarms}} }
        }
      ]
    }
    """;

    [Fact]
    public async Task Root_wrapped_fatal_reaches_the_caller_as_a_blocking_global_alarm()
    {
        var json = RootAlarmPayload("""
          "alarms": {
            "fatals": [{ "code": 52101, "desc": "2000-1, have no charge point in map.", "times": 1, "timestamp": 1635409588 }],
            "errors": [], "warnings": [], "notices": []
          },
        """);
        var fleet = await Client(new RecordingHandler(_ => Json(json))).GetRobotsStatusAsync(Guid.NewGuid());

        var alarm = Assert.Single(fleet.GlobalAlarms);
        Assert.Equal("52101", alarm.Code);
        Assert.Equal(RdsAlarmSeverity.Fatal, alarm.Severity);
        Assert.True(fleet.GlobalHasBlockingAlarm);
        // The robot itself is clean: the core alarm is NOT attributed to it.
        Assert.False(Assert.Single(fleet.Robots).HasBlockingAlarm);
    }

    [Fact]
    public async Task Root_direct_error_reaches_the_caller_as_a_blocking_global_alarm()
    {
        // Shape B, exactly as the documented response carries it beside "alarms".
        var json = RootAlarmPayload("""
          "errors": [{ "52103": 1631943027, "desc": "Rose_01,SW500_3, have no park point in map.", "times": 1 }],
        """);
        var fleet = await Client(new RecordingHandler(_ => Json(json))).GetRobotsStatusAsync(Guid.NewGuid());

        var alarm = Assert.Single(fleet.GlobalAlarms);
        Assert.Equal("52103", alarm.Code);
        Assert.Equal(RdsAlarmSeverity.Error, alarm.Severity);
        Assert.True(fleet.GlobalHasBlockingAlarm);
    }

    [Theory]
    [InlineData("warnings", RdsAlarmSeverity.Warning)]
    [InlineData("notices", RdsAlarmSeverity.Notice)]
    public async Task Root_warning_and_notice_are_reported_but_not_blocking(string bucket, RdsAlarmSeverity expected)
    {
        var json = RootAlarmPayload($$"""
          "alarms": { "{{bucket}}": [{ "code": 55001, "desc": "dispatching state.", "times": 1 }] },
        """);
        var fleet = await Client(new RecordingHandler(_ => Json(json))).GetRobotsStatusAsync(Guid.NewGuid());

        Assert.Equal(expected, Assert.Single(fleet.GlobalAlarms).Severity);
        Assert.False(fleet.GlobalHasBlockingAlarm);
    }

    [Fact]
    public async Task A_robot_fatal_still_blocks_on_the_robot_and_not_globally()
    {
        const string robotFatal = """, "alarms": { "fatals": [{ "code": 52201, "desc": "robot fault", "times": 1 }] }""";
        var json = RootAlarmPayload(string.Empty, robotFatal);
        var fleet = await Client(new RecordingHandler(_ => Json(json))).GetRobotsStatusAsync(Guid.NewGuid());

        Assert.True(Assert.Single(fleet.Robots).HasBlockingAlarm);
        Assert.False(fleet.GlobalHasBlockingAlarm);
        Assert.Empty(fleet.GlobalAlarms);
    }

    [Fact]
    public async Task Root_and_robot_alarms_are_both_preserved_and_kept_apart()
    {
        const string rootError = "\"alarms\": { \"errors\": [{ \"code\": 52101, \"desc\": \"core\", \"times\": 1 }] },";
        const string robotWarning = """, "alarms": { "warnings": [{ "code": 55001, "desc": "robot warning", "times": 1 }] }""";
        var json = RootAlarmPayload(rootError, robotWarning);
        var fleet = await Client(new RecordingHandler(_ => Json(json))).GetRobotsStatusAsync(Guid.NewGuid());

        Assert.Equal("52101", Assert.Single(fleet.GlobalAlarms).Code);
        Assert.True(fleet.GlobalHasBlockingAlarm);

        var robot = Assert.Single(fleet.Robots);
        Assert.Equal("55001", Assert.Single(robot.Alarms).Code);
        Assert.False(robot.HasBlockingAlarm);
    }

    [Fact]
    public async Task A_malformed_report_does_not_discard_a_root_fatal()
    {
        // The safety signal must survive a robot array we cannot read at all.
        var json = """
        {
          "code": 0,
          "msg": "ok",
          "alarms": { "fatals": [{ "code": 52101, "desc": "core fatal", "times": 1 }] },
          "report": "not-an-array"
        }
        """;
        var fleet = await Client(new RecordingHandler(_ => Json(json))).GetRobotsStatusAsync(Guid.NewGuid());

        Assert.Empty(fleet.Robots);
        Assert.True(fleet.GlobalHasBlockingAlarm);
        Assert.Equal("52101", Assert.Single(fleet.GlobalAlarms).Code);
    }

    [Fact]
    public async Task Get_robots_status_parses_live_report_telemetry_for_Sfl_Cbd15()
    {
        var json = """
        {
          "code": 0,
          "msg": "ok",
          "report": [
            {
              "vehicle_id": "Sfl-Cbd15",
              "connection_status": 1,
              "dispatchable": true,
              "isLoaded": false,
              "is_error": false,
              "remaining_time": 42.5,
              "basic_info": {
                "model": "HELIFORK",
                "current_map": "26.7.2026_map_optimak"
              },
              "procBusiness": false,
              "rbk_report": {
                "current_station": "PP80",
                "last_station": "LM12",
                "battery_level": 0.98,
                "x": 2.304,
                "y": 4.691,
                "angle": 1.5708,
                "fatals": [],
                "errors": [
                  {
                    "code": 52201,
                    "desc": "documented shape A entry.",
                    "times": 1,
                    "timestamp": 1681110452
                  }
                ],
                "warnings": [
                  {
                    "55001": 1634020734,
                    "desc": "documented shape B entry.",
                    "times": 1
                  }
                ],
                "notices": []
              },
              "current_order": {
                "id": "order-123",
                "state": "RUNNING"
              }
            }
          ]
        }
        """;
        var handler = new RecordingHandler(_ => Json(json));
        var client = Client(handler);

        var robots = (await client.GetRobotsStatusAsync(Guid.NewGuid())).Robots;

        var robot = Assert.Single(robots);
        Assert.Equal("Sfl-Cbd15", robot.VehicleId);
        Assert.Equal("HELIFORK", robot.Model);
        Assert.Equal("26.7.2026_map_optimak", robot.CurrentMap);
        Assert.Equal("PP80", robot.CurrentStation);
        Assert.Equal("LM12", robot.LastStation);
        Assert.Equal(1, robot.ConnectionStatus);
        Assert.True(robot.Dispatchable);
        Assert.False(robot.IsLoaded);
        Assert.Equal(0.98, robot.BatteryLevel);
        Assert.Equal(2.304, robot.X);
        Assert.Equal(4.691, robot.Y);
        Assert.Equal(1.5708, robot.Angle);
        Assert.Equal(42.5, robot.RemainingTime);
        Assert.Equal("order-123", robot.CurrentOrderId);
        Assert.Equal("RUNNING", robot.CurrentOrderState);
        Assert.False(robot.ProcBusiness);
        Assert.False(robot.MayBeExecutingBusinessWork);

        // Both documented entry shapes parse, and severity survives: the shape-A
        // error blocks, the shape-B warning does not.
        var error = Assert.Single(robot.Alarms, alarm => alarm.Severity == RdsAlarmSeverity.Error);
        Assert.Equal("52201", error.Code);
        Assert.True(error.IsBlocking);
        var warning = Assert.Single(robot.Alarms, alarm => alarm.Severity == RdsAlarmSeverity.Warning);
        Assert.Equal("55001", warning.Code);
        Assert.False(warning.IsBlocking);
        Assert.True(robot.HasBlockingAlarm);
    }

    [Fact]
    public async Task Live_rdscore_telemetry_can_be_queried_if_server_is_running()
    {
        using var testClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            using var ping = await testClient.GetAsync("http://127.0.0.1:8088/robotsStatus");
            if (!ping.IsSuccessStatusCode) return;
        }
        catch
        {
            return; // RDSCore not running in current environment
        }

        var options = new RdsCoreExecutionOptions(true, RdsExecutionMode.RdsCoreNativeOrder, "http://127.0.0.1:8088/", 5, true);
        var client = new RdsCoreNativeOrderClient(new LiveFactory(options.BaseAddress), options);

        var robots = (await client.GetRobotsStatusAsync(Guid.NewGuid())).Robots;
        Assert.NotEmpty(robots);
        var robot = Assert.Single(robots, r => r.VehicleId == "Sfl-Cbd15");
        Assert.Equal("Sfl-Cbd15", robot.VehicleId);
        Assert.True(robot.BatteryLevel > 0);
    }

    private static RdsCoreNormalOrder NavigationOrder() => RdsCoreNormalOrder.Create(
        RequestId,
        Navigation("pickup", "SIM-A"),
        Navigation("dropoff", "SIM-B"));

    private static RdsCoreNavigationBlock Navigation(string suffix, string location) => new(Block(suffix), location);
    private static string Block(string suffix) => $"{RequestId:D}:{suffix}";
    private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task Remaining_time_provider_keys_by_the_shared_vehicle_space_identity()
    {
        // The ETA map must key by the same vehicle-space identifier that
        // rds_tasks.agv_id and the mutation APIs use, so enrichment cannot miss on
        // an identity-space mismatch. The simulator reports uuid == vehicle_id,
        // which is exactly why the previously-inlined parser went unnoticed.
        var handler = new RecordingHandler(_ => Json("""
            {"report":[
                {"uuid":"ROBOT-INTERNAL-123","vehicle_id":"AGV-01","remaining_time":42.5},
                {"uuid":"ROBOT-INTERNAL-999","remaining_time":7.0},
                {"uuid":"ROBOT-NEGATIVE","vehicle_id":"AGV-03","remaining_time":-1.0}
            ]}
            """));
        var options = new RdsCoreExecutionOptions(true, RdsExecutionMode.RdsCoreNativeOrder, "http://127.0.0.1:18088/", 5, true);
        options.Validate();
        var provider = new RdsCoreRobotTelemetryProvider(
            new Factory(handler, options.BaseAddress),
            options,
            NullLogger<RdsCoreRobotTelemetryProvider>.Instance);

        var result = await provider.GetRobotRemainingTimesAsync();

        // Vehicle-space wins when both are present.
        Assert.Equal(42.5, result["AGV-01"]);
        Assert.False(result.ContainsKey("ROBOT-INTERNAL-123"));
        // uuid remains the fallback when vehicle_id is absent.
        Assert.Equal(7.0, result["ROBOT-INTERNAL-999"]);
        // Negative remaining_time is still not an ETA.
        Assert.False(result.ContainsKey("AGV-03"));
    }

    private static RdsCoreNativeOrderClient Client(RecordingHandler handler, RdsCoreExecutionOptions? options = null)
    {
        options ??= new(true, RdsExecutionMode.RdsCoreNativeOrder, "http://127.0.0.1:18088/", 5, true);
        options.Validate();
        return new(new Factory(handler, options.BaseAddress), options);
    }

    private static HttpResponseMessage Json(string value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(value) };

    private sealed class Factory(HttpMessageHandler handler, Uri? baseAddress) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = baseAddress };
    }

    private sealed class LiveFactory(Uri? baseAddress) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { BaseAddress = baseAddress };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        public List<ObservedRequest> Requests { get; } = [];
        public ObservedRequest Single() => Assert.Single(Requests);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(
                request.Method,
                request.RequestUri!.PathAndQuery,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return route(request);
        }
    }

    private sealed record ObservedRequest(HttpMethod Method, string Path, string Body);
}
