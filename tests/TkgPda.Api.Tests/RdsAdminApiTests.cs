using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TkgPda.Application;
using Xunit;

namespace TkgPda.Api.Tests;

public sealed class RdsAdminApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    public async Task InitializeAsync() => await fixture.ResetAsync();
    public async Task DisposeAsync() => await fixture.ResetAsync();

    [Fact]
    public async Task Commissioning_capture_is_admin_only_explicit_and_disabled_by_default()
    {
        var operatorIdentity = await fixture.SeedIdentityAsync();
        using var operatorClient = await fixture.LoginClientAsync(operatorIdentity);
        var denied = await operatorClient.PostAsJsonAsync(
            "/api/v1/admin/rds/commissioning/capture",
            new { confirmReadOnlyDiscovery = true });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await fixture.ResetAsync();
        var adminIdentity = await fixture.SeedIdentityAsync(RoleCodes.Admin, machineCode: null);
        using var adminClient = await fixture.AdminLoginClientAsync(adminIdentity);
        var unconfirmed = await adminClient.PostAsJsonAsync(
            "/api/v1/admin/rds/commissioning/capture",
            new { confirmReadOnlyDiscovery = false });
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Equal("RDSCORE_DISCOVERY_CONFIRMATION_REQUIRED", await ApiTestFixture.ErrorCodeAsync(unconfirmed));

        var disabled = await adminClient.PostAsJsonAsync(
            "/api/v1/admin/rds/commissioning/capture",
            new { confirmReadOnlyDiscovery = true });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);
        Assert.Equal("RDSCORE_DISCOVERY_DISABLED", await ApiTestFixture.ErrorCodeAsync(disabled));
        Assert.Equal(0, fixture.RdsCoreDiscoveryClient.Calls);
        Assert.Equal(0, await fixture.ScalarAsync<long>(
            "SELECT count(*) FROM audit.audit_logs WHERE action = 'RDS_COMMISSIONING_DISCOVERY_CAPTURED'"));

        fixture.RdsCoreDiscoveryClient.Enabled = true;
        var captured = await adminClient.PostAsJsonAsync(
            "/api/v1/admin/rds/commissioning/capture",
            new { confirmReadOnlyDiscovery = true });
        captured.EnsureSuccessStatusCode();
        Assert.Equal(1, fixture.RdsCoreDiscoveryClient.Calls);
        Assert.Equal(1, await fixture.ScalarAsync<long>(
            "SELECT count(*) FROM audit.audit_logs WHERE action = 'RDS_COMMISSIONING_DISCOVERY_CAPTURED' AND actor_user_id = @user",
            ("user", adminIdentity.UserId)));
    }

    [Fact]
    public async Task Rds_diagnostics_are_admin_web_only_and_use_safe_endpoint_display()
    {
        var operatorIdentity = await fixture.SeedIdentityAsync();
        var operatorClient = await fixture.LoginClientAsync(operatorIdentity);
        var denied = await operatorClient.GetAsync("/api/v1/admin/rds/status");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await fixture.ResetAsync();
        var admin = await fixture.SeedIdentityAsync(RoleCodes.Admin, machineCode: null);
        var client = await fixture.AdminLoginClientAsync(admin);
        var response = await client.GetAsync("/api/v1/admin/rds/status");
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<RdsConnectionStatusRecord>();

        Assert.NotNull(status);
        Assert.True(status.Reachable);
        Assert.Equal("http://rds.test:8080", status.BaseAddressDisplaySafe);
        Assert.Equal(["/api/work-sites/sites"], fixture.RdsClient.Calls);
    }

    [Fact]
    public async Task Explicit_profile_sync_is_idempotent_and_filterable()
    {
        var setup = await SetupAsync();
        fixture.RdsClient.WorkSites =
        [
            Site("P08A-PHYSICAL-1", 1, disabled: 0, syncFailed: 0),
            Site("P08A-LOGICAL-1", 0, disabled: 0, syncFailed: 0)
        ];

        var first = await setup.Client.PostAsJsonAsync("/api/v1/admin/rds/worksites/sync", new { navigationProfileId = setup.NavigationProfileId });
        first.EnsureSuccessStatusCode();
        var firstResult = await first.Content.ReadFromJsonAsync<RdsWorkSiteSyncRecord>();
        Assert.Equal(2, firstResult?.Created);
        var second = await setup.Client.PostAsJsonAsync("/api/v1/admin/rds/worksites/sync", new { navigationProfileId = setup.NavigationProfileId });
        second.EnsureSuccessStatusCode();
        var secondResult = await second.Content.ReadFromJsonAsync<RdsWorkSiteSyncRecord>();
        Assert.Equal(0, secondResult?.Created);
        Assert.Equal(2, secondResult?.Updated);

        var list = await setup.Client.GetFromJsonAsync<RdsMirroredWorkSiteRecord[]>($"/api/v1/admin/rds/worksites?navigationProfileId={setup.NavigationProfileId}&type=1");
        var physical = Assert.Single(list!);
        Assert.Equal("P08A-PHYSICAL-1", physical.SiteId);
        Assert.Equal(2, await fixture.ScalarAsync<long>("SELECT count(*) FROM integration.rds_worksites WHERE navigation_profile_id = @id", ("id", setup.NavigationProfileId)));
    }

    [Fact]
    public async Task Mapping_is_manual_audited_and_unmapping_is_supported()
    {
        var setup = await SetupAsync();
        fixture.RdsClient.WorkSites = [Site("P08A-DISABLED-1", 1, disabled: 1, syncFailed: 0)];
        (await setup.Client.PostAsJsonAsync("/api/v1/admin/rds/worksites/sync", new { navigationProfileId = setup.NavigationProfileId })).EnsureSuccessStatusCode();
        var workSite = Assert.Single((await setup.Client.GetFromJsonAsync<RdsMirroredWorkSiteRecord[]>($"/api/v1/admin/rds/worksites?navigationProfileId={setup.NavigationProfileId}"))!);
        var slotId = (await fixture.LogisticsSlotsAsync(setup.LayoutId, 1)).Single();

        var mapped = await setup.Client.PutAsJsonAsync($"/api/v1/admin/layout-slots/{slotId}/rds-mapping", new { rdsWorksiteId = workSite.Id });
        mapped.EnsureSuccessStatusCode();
        var mapping = await mapped.Content.ReadFromJsonAsync<RdsSlotMappingRecord>();
        Assert.Equal("P08A-DISABLED-1", mapping?.RdsSiteId);
        Assert.False(mapping?.ExecutionReady);
        Assert.Equal(1, await fixture.ScalarAsync<long>("SELECT count(*) FROM audit.audit_logs WHERE action = 'RDS_WORKSITE_MAPPING_SET' AND entity_id = @id", ("id", slotId.ToString())));

        var removed = await setup.Client.DeleteAsync($"/api/v1/admin/layout-slots/{slotId}/rds-mapping");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(1, await fixture.ScalarAsync<long>("SELECT count(*) FROM audit.audit_logs WHERE action = 'RDS_WORKSITE_MAPPING_REMOVED' AND entity_id = @id", ("id", slotId.ToString())));
    }

    [Fact]
    public async Task Machine_region_and_logical_worksite_mapping_are_rejected()
    {
        var setup = await SetupAsync();
        fixture.RdsClient.WorkSites =
        [
            Site("P08A-PHYSICAL-2", 1, disabled: 0, syncFailed: 0),
            Site("P08A-LOGICAL-2", 0, disabled: 0, syncFailed: 0)
        ];
        (await setup.Client.PostAsJsonAsync("/api/v1/admin/rds/worksites/sync", new { navigationProfileId = setup.NavigationProfileId })).EnsureSuccessStatusCode();
        var sites = (await setup.Client.GetFromJsonAsync<RdsMirroredWorkSiteRecord[]>($"/api/v1/admin/rds/worksites?navigationProfileId={setup.NavigationProfileId}"))!;
        var machineSlot = await fixture.ScalarAsync<Guid>("SELECT id FROM master.layout_slots WHERE layout_id = @id AND slot_role = 'MACHINE' LIMIT 1", ("id", setup.LayoutId));
        var logisticsSlot = (await fixture.LogisticsSlotsAsync(setup.LayoutId, 1)).Single();

        var machine = await setup.Client.PutAsJsonAsync($"/api/v1/admin/layout-slots/{machineSlot}/rds-mapping", new { rdsWorksiteId = sites.Single(site => site.Type == 1).Id });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, machine.StatusCode);
        Assert.Equal("MACHINE_REGION_MAPPING_FORBIDDEN", await ApiTestFixture.ErrorCodeAsync(machine));
        var logical = await setup.Client.PutAsJsonAsync($"/api/v1/admin/layout-slots/{logisticsSlot}/rds-mapping", new { rdsWorksiteId = sites.Single(site => site.Type == 0).Id });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, logical.StatusCode);
        Assert.Equal("RDS_WORKSITE_NOT_PHYSICAL", await ApiTestFixture.ErrorCodeAsync(logical));
    }

    private async Task<Setup> SetupAsync()
    {
        var identity = await fixture.SeedIdentityAsync(RoleCodes.Admin, machineCode: null);
        var client = await fixture.AdminLoginClientAsync(identity);
        var layout = await fixture.ContextAsync("SH-01-A", "1");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var navigation = await fixture.ScalarAsync<Guid>(
            """
            INSERT INTO master.navigation_profiles (machine_id, code, name, status, created_by)
            VALUES (@machine, @code, 'Phase 08A API Test', 'DRAFT', @user)
            RETURNING id
            """,
            ("machine", layout.MachineId), ("code", $"P08A-{suffix}"), ("user", identity.UserId));
        return new(client, navigation, layout.LayoutId);
    }

    private static RdsWorkSiteSnapshot Site(string id, int type, int disabled, int syncFailed)
    {
        using var json = JsonDocument.Parse($$"""{"siteId":"{{id}}","type":{{type}},"filled":0,"locked":0,"disabled":{{disabled}},"syncFailed":{{syncFailed}},"content":""}""");
        return new(id, "A", "G", type, 0, 0, null, disabled, syncFailed, string.Empty, null, json.RootElement.Clone());
    }

    private sealed record Setup(HttpClient Client, Guid NavigationProfileId, Guid LayoutId);
}
