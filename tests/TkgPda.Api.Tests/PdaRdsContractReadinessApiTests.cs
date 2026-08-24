using System.Net;
using System.Net.Http.Json;
using TkgPda.Api;
using TkgPda.Application;
using Xunit;

namespace TkgPda.Api.Tests;

public sealed class PdaRdsContractReadinessApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    [Fact]
    public async Task Active_variant_product_groups_expose_main_and_active_child_parts_with_null_evidence_preserved()
    {
        await fixture.ResetAsync();
        const string machineCode = "SH-02-P1 -SH-02-P2";
        var identity = await fixture.SeedIdentityAsync(machineCode: machineCode);
        var machine = await fixture.ConfigureMachineAsync(machineCode: machineCode);
        using var client = await fixture.LoginClientAsync(identity);

        var context = await GetContextAsync(client, machine.MachineId);
        var expectedGroups = await fixture.ScalarAsync<long>(
            "SELECT count(*) FROM master.product_groups WHERE variant_id = @variant_id AND is_active",
            ("variant_id", machine.VariantId));
        var expectedParts = await fixture.ScalarAsync<long>(
            """
            SELECT count(*) FROM master.product_group_parts gpp
            JOIN master.product_groups pg ON pg.id = gpp.product_group_id
            JOIN master.parts p ON p.id = gpp.part_id AND p.is_active
            WHERE pg.variant_id = @variant_id AND pg.is_active AND gpp.is_active
            """,
            ("variant_id", machine.VariantId));
        var nullEvidence = await fixture.ScalarAsync<long>(
            """
            SELECT count(*) FROM master.product_group_parts gpp
            JOIN master.product_groups pg ON pg.id = gpp.product_group_id
            JOIN master.parts p ON p.id = gpp.part_id AND p.is_active
            WHERE pg.variant_id = @variant_id AND pg.is_active AND gpp.is_active
              AND (gpp.container_type_id IS NULL OR gpp.lot_quantity IS NULL)
            """,
            ("variant_id", machine.VariantId));

        Assert.Equal(expectedGroups, context.ProductGroups.Count);
        Assert.Equal(expectedParts, context.ProductGroups.Sum(group => group.Parts.Count));
        Assert.All(context.ProductGroups, group =>
        {
            Assert.NotEqual(Guid.Empty, group.MainPartId);
            Assert.False(string.IsNullOrWhiteSpace(group.MainPartCode));
        });
        Assert.True(nullEvidence > 0);
        Assert.Contains(context.ProductGroups.SelectMany(group => group.Parts), part =>
            part.ContainerTypeId is null || part.LotQuantity is null);
        Assert.Empty(context.TaskTemplates);

        await fixture.ExecuteAsync(
            """
            INSERT INTO master.task_templates (
                variant_id, task_type_id, rds_task_label, priority, config_json, is_active)
            SELECT @variant_id, id, NULL, 25, '{"testScope":"P07A"}'::jsonb, true
            FROM master.task_types WHERE code = 'BRING_EMPTY'
            """,
            ("variant_id", machine.VariantId));

        var contextWithTemplate = await GetContextAsync(client, machine.MachineId);
        var template = Assert.Single(contextWithTemplate.TaskTemplates);
        Assert.Equal("BRING_EMPTY", template.Operation);
        Assert.Null(template.SourceSlotGroupId);
        Assert.Null(template.DestinationSlotGroupId);
        Assert.Null(template.RdsTaskLabel);
        Assert.False(template.RdsTaskLabelConfigured);
    }

    [Fact]
    public async Task Available_actions_are_role_permission_state_and_machine_region_sensitive()
    {
        await fixture.ResetAsync();
        var line = await fixture.SeedIdentityAsync(role: RoleCodes.LineOperator);
        var supervisor = await fixture.SeedIdentityAsync(role: RoleCodes.Supervisor);
        var warehouse = await fixture.SeedIdentityAsync(role: RoleCodes.WarehouseOperator);
        var admin = await fixture.SeedIdentityAsync(role: RoleCodes.Admin, machineCode: null);
        var machine = await fixture.ConfigureMachineAsync();
        var slots = await fixture.LogisticsSlotsAsync(machine.LayoutId, 3);
        Assert.Equal(3, slots.Length);
        await fixture.SetSlotAsync(slots[0], "VACANT", "UNKNOWN");
        await fixture.SetSlotAsync(slots[1], "OCCUPIED", "EMPTY");
        await fixture.SetSlotAsync(slots[2], "OCCUPIED", "FULL");

        using var lineClient = await fixture.LoginClientAsync(line);
        var lineContext = await GetContextAsync(lineClient, machine.MachineId);
        AssertEnabled(lineContext, slots[0], "BRING_EMPTY");
        AssertEnabled(lineContext, slots[1], "REMOVE_EMPTY");
        AssertEnabled(lineContext, slots[2], "REMOVE_FULL");

        using var supervisorClient = await fixture.LoginClientAsync(supervisor);
        var supervisorContext = await GetContextAsync(supervisorClient, machine.MachineId);
        AssertEnabled(supervisorContext, slots[1], "REMOVE_EMPTY");
        AssertEnabled(supervisorContext, slots[2], "REMOVE_FULL");

        using var adminPdaClient = await fixture.LoginClientAsync(admin);
        var adminPdaContext = await GetContextAsync(adminPdaClient, machine.MachineId);
        AssertEnabled(adminPdaContext, slots[0], "BRING_EMPTY");
        AssertEnabled(adminPdaContext, slots[2], "REMOVE_FULL");

        using var warehouseClient = await fixture.LoginClientAsync(warehouse);
        var warehouseContext = await GetContextAsync(warehouseClient, machine.MachineId);
        AssertEnabled(warehouseContext, slots[0], "BRING_EMPTY");
        AssertDisabled(warehouseContext, slots[1], "REMOVE_EMPTY", "OPERATION_NOT_ALLOWED_FOR_ROLE");
        AssertDisabled(warehouseContext, slots[2], "REMOVE_FULL", "OPERATION_NOT_ALLOWED_FOR_ROLE");

        var machineRegion = lineContext.Slots.Single(slot => slot.Role == "MACHINE");
        Assert.False(machineRegion.TransportEligible);
        Assert.All(machineRegion.AvailableActions, action =>
        {
            Assert.False(action.Enabled);
            Assert.Equal("MACHINE_REGION_FORBIDDEN", action.DisabledCode);
        });

        using var adminWebClient = await fixture.AdminLoginClientAsync(admin);
        var adminWebContext = await GetContextAsync(adminWebClient, machine.MachineId);
        Assert.All(adminWebContext.Slots.SelectMany(slot => slot.AvailableActions), action =>
        {
            Assert.False(action.Enabled);
            Assert.Equal("PDA_DEVICE_REQUIRED", action.DisabledCode);
        });
    }

    [Fact]
    public async Task Active_request_projection_sets_is_mine_filters_history_and_blocks_duplicate_slot_action()
    {
        await fixture.ResetAsync();
        var owner = await fixture.SeedIdentityAsync();
        var viewer = await fixture.SeedIdentityAsync();
        var machine = await fixture.ConfigureMachineAsync();
        var slot = (await fixture.LogisticsSlotsAsync(machine.LayoutId, 1)).Single();
        await fixture.SetSlotAsync(slot, "VACANT", "UNKNOWN");
        using var ownerClient = await fixture.LoginClientAsync(owner);
        var createdResponse = await fixture.SendTransportAsync(ownerClient, Bring(machine, slot), "p07a-active-owner");
        createdResponse.EnsureSuccessStatusCode();
        var created = await createdResponse.Content.ReadFromJsonAsync<TransportHttpResponse>();
        Assert.NotNull(created);
        await fixture.ExecuteAsync(
            """
            INSERT INTO ops.transport_requests (
                idempotency_key, request_origin, requested_by, machine_id, variant_id, layout_id,
                product_group_id, part_id, container_type_id, task_type_id,
                source_slot_id, destination_slot_id, status, priority, completed_at)
            SELECT 'p07a-historical', request_origin, requested_by, machine_id, variant_id, layout_id,
                   product_group_id, part_id, container_type_id, task_type_id,
                   source_slot_id, NULL, 'COMPLETED', priority, now()
            FROM ops.transport_requests WHERE id = @request_id
            """,
            ("request_id", created.Id));

        var ownerContext = await GetContextAsync(ownerClient, machine.MachineId);
        var ownerRequest = Assert.Single(ownerContext.ActiveTransportRequests);
        Assert.Equal(created.Id, ownerRequest.RequestId);
        Assert.True(ownerRequest.IsMine);
        Assert.Equal("QUEUED", ownerRequest.Status);
        Assert.Equal("BRING_EMPTY", ownerRequest.Operation);
        Assert.True(ownerRequest.CanCancel);
        var reservedSlot = ownerContext.Slots.Single(item => item.Id == slot);
        Assert.All(reservedSlot.AvailableActions, action => Assert.Equal("SLOT_BUSY", action.DisabledCode));

        using var viewerClient = await fixture.LoginClientAsync(viewer);
        var viewerContext = await GetContextAsync(viewerClient, machine.MachineId);
        Assert.False(Assert.Single(viewerContext.ActiveTransportRequests).IsMine);
    }

    [Fact]
    public async Task Persisted_rds_task_and_worksite_are_projected_while_unmapped_slot_remains_honestly_unmapped()
    {
        await fixture.ResetAsync();
        var identity = await fixture.SeedIdentityAsync(role: RoleCodes.Supervisor);
        var machine = await fixture.ConfigureMachineAsync();
        var slots = await fixture.LogisticsSlotsAsync(machine.LayoutId, 2);
        await fixture.SetSlotAsync(slots[0], "VACANT", "UNKNOWN");
        await fixture.SetSlotAsync(slots[1], "OCCUPIED", "EMPTY");
        using var client = await fixture.LoginClientAsync(identity);
        var createdResponse = await fixture.SendTransportAsync(client, Bring(machine, slots[0]), "p07a-rds-projection");
        createdResponse.EnsureSuccessStatusCode();
        var created = await createdResponse.Content.ReadFromJsonAsync<TransportHttpResponse>();
        Assert.NotNull(created);
        await fixture.ExecuteAsync(
            "UPDATE ops.transport_requests SET status = 'SENT_TO_RDS', updated_at = now() WHERE id = @id",
            ("id", created.Id));
        var workSiteId = await fixture.ScalarAsync<Guid>(
            """
            INSERT INTO integration.rds_worksites (
                rds_site_id, area, group_name, rds_type, last_filled, last_locked,
                last_disabled, last_content, last_agv_id, last_sync_failed, last_sync_at)
            VALUES ('P07A-SITE-01', 'LINE-A', 'P07A-GROUP', 7, 0, 1, 0,
                    'persisted-content', 'AGV-42', 1, now())
            RETURNING id
            """);
        await fixture.ExecuteAsync(
            "INSERT INTO integration.layout_slot_rds_mapping (layout_slot_id, rds_worksite_id) VALUES (@slot_id, @worksite_id)",
            ("slot_id", slots[0]),
            ("worksite_id", workSiteId));
        await fixture.ExecuteAsync(
            """
            INSERT INTO integration.rds_tasks (
                transport_request_id, rds_task_record_id, rds_task_label, out_order_no,
                agv_id, rds_status, sent_at, last_sync_at)
            VALUES (@request_id, 'RDS-P07A-42', 'PERSISTED-LABEL', 'OUT-P07A-42',
                    'AGV-42', 1000, now(), now())
            """,
            ("request_id", created.Id));

        var context = await GetContextAsync(client, machine.MachineId);
        var mapped = context.Slots.Single(slot => slot.Id == slots[0]);
        Assert.True(mapped.RdsWorkSite.IsMapped);
        Assert.Equal("P07A-SITE-01", mapped.RdsWorkSite.SiteId);
        Assert.Equal("LINE-A", mapped.RdsWorkSite.Area);
        Assert.Equal("P07A-GROUP", mapped.RdsWorkSite.GroupName);
        Assert.Equal(7, mapped.RdsWorkSite.Type);
        Assert.Equal(0, mapped.RdsWorkSite.Filled);
        Assert.Equal(1, mapped.RdsWorkSite.Locked);
        Assert.Equal(0, mapped.RdsWorkSite.Disabled);
        Assert.Equal(1, mapped.RdsWorkSite.SyncFailed);
        Assert.Equal("persisted-content", mapped.RdsWorkSite.Content);
        Assert.Equal("AGV-42", mapped.RdsWorkSite.AgvId);
        Assert.NotNull(mapped.RdsWorkSite.LastSyncAt);

        var unmapped = context.Slots.Single(slot => slot.Id == slots[1]);
        Assert.False(unmapped.RdsWorkSite.IsMapped);
        Assert.Null(unmapped.RdsWorkSite.SiteId);
        Assert.Equal("RDS_WORKSITE_NOT_MAPPED", unmapped.ExecutionReadiness.ReadinessCode);

        var request = Assert.Single(context.ActiveTransportRequests);
        Assert.Equal("ROBOT_ASSIGNED", request.Execution.State);
        Assert.Equal("RDS-P07A-42", request.Execution.RdsTaskRecordId);
        Assert.Equal("PERSISTED-LABEL", request.Execution.RdsTaskLabel);
        Assert.Equal("OUT-P07A-42", request.Execution.OutOrderNo);
        Assert.Equal("AGV-42", request.Execution.AgvId);
        Assert.Equal(1000, request.Execution.RdsStatus);
        Assert.False(request.CanCancel);
        Assert.Equal("CANCELLATION_AFTER_EXECUTION_DEFERRED", request.CancelDisabledCode);
    }

    private static async Task<OperationalContextRecord> GetContextAsync(HttpClient client, Guid machineId)
    {
        var response = await client.GetAsync($"/api/v1/machines/{machineId}/operational-context");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<OperationalContextRecord>()
            ?? throw new InvalidOperationException("Operational context response was empty.");
    }

    private static void AssertEnabled(OperationalContextRecord context, Guid slotId, string operation)
    {
        var action = context.Slots.Single(slot => slot.Id == slotId).AvailableActions.Single(item => item.Action == operation);
        Assert.True(action.Enabled);
        Assert.Null(action.DisabledCode);
    }

    private static void AssertDisabled(OperationalContextRecord context, Guid slotId, string operation, string code)
    {
        var action = context.Slots.Single(slot => slot.Id == slotId).AvailableActions.Single(item => item.Action == operation);
        Assert.False(action.Enabled);
        Assert.Equal(code, action.DisabledCode);
    }

    private static CreateTransportHttpRequest Bring(LayoutContext context, Guid destination) => new(
        context.MachineId,
        context.VariantId,
        context.LayoutId,
        null,
        null,
        null,
        null,
        "BRING_EMPTY",
        null,
        destination);
}
