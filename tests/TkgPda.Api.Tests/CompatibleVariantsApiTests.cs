using System.Net;
using System.Net.Http.Json;
using TkgPda.Application;
using Xunit;

namespace TkgPda.Api.Tests;

// WP-02 — machine + part -> compatible variant projection, derived only from
// existing normalized master data (master.variants / product_groups /
// product_group_parts). No Flutter/RDS involvement.
public sealed class CompatibleVariantsApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private const string MachineCode = "SH-01-A";

    [Fact]
    public async Task Main_part_resolves_its_variant()
    {
        var scene = await SeedSceneAsync();

        var variants = await QueryAsync(scene.MachineId, scene.MainPartId);

        var only = Assert.Single(variants);
        Assert.Equal(scene.V1Id, only.VariantId);
        Assert.Equal("MAIN", only.RelationType);
    }

    [Fact]
    public async Task Related_part_resolves_its_variant()
    {
        var scene = await SeedSceneAsync();

        var variants = await QueryAsync(scene.MachineId, scene.RelatedPartId);

        var only = Assert.Single(variants);
        Assert.Equal(scene.V1Id, only.VariantId);
        Assert.Equal("RELATED", only.RelationType);
    }

    [Fact]
    public async Task Alt_part_resolves_its_variant()
    {
        var scene = await SeedSceneAsync();

        var variants = await QueryAsync(scene.MachineId, scene.AltPartId);

        var only = Assert.Single(variants);
        Assert.Equal(scene.V1Id, only.VariantId);
        Assert.Equal("ALT_PART", only.RelationType);
    }

    [Fact]
    public async Task Part_compatible_with_multiple_variants_returns_all_of_them()
    {
        var scene = await SeedSceneAsync();

        var variants = await QueryAsync(scene.MachineId, scene.SharedPartId);

        Assert.Equal(
            new[] { scene.V2Id, scene.V1Id, scene.V3Id },
            variants.Select(v => v.VariantId).ToArray());
    }

    [Fact]
    public async Task Duplicate_relationship_paths_do_not_duplicate_the_variant()
    {
        var scene = await SeedSceneAsync();

        var variants = await QueryAsync(scene.MachineId, scene.DuplicatedPartId);

        var only = Assert.Single(variants);
        Assert.Equal(scene.V1Id, only.VariantId);
    }

    [Fact]
    public async Task Part_with_no_mapping_returns_an_empty_list()
    {
        var scene = await SeedSceneAsync();

        var variants = await QueryAsync(scene.MachineId, scene.OrphanPartId);

        Assert.Empty(variants);
    }

    [Fact]
    public async Task Unauthorized_machine_is_rejected()
    {
        var scene = await SeedSceneAsync();
        var identity = await fixture.SeedIdentityAsync(machineCode: null);
        using var client = await fixture.LoginClientAsync(identity);

        var response = await client.GetAsync(
            $"/api/v1/machines/{scene.MachineId}/parts/{scene.MainPartId}/compatible-variants");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("MACHINE_ACCESS_DENIED", await ApiTestFixture.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Authorized_machine_returns_the_compatible_variants()
    {
        var scene = await SeedSceneAsync();

        var variants = await QueryAsync(scene.MachineId, scene.MainPartId);

        Assert.NotEmpty(variants);
    }

    [Fact]
    public async Task Ordering_is_deterministic_by_display_order()
    {
        var scene = await SeedSceneAsync();

        var first = await QueryAsync(scene.MachineId, scene.SharedPartId);
        var second = await QueryAsync(scene.MachineId, scene.SharedPartId);

        Assert.Equal(
            first.Select(v => v.VariantId).ToArray(),
            second.Select(v => v.VariantId).ToArray());
        Assert.Equal(new[] { scene.V2Id, scene.V1Id, scene.V3Id }, first.Select(v => v.VariantId).ToArray());
    }

    private async Task<List<CompatibleVariantRecord>> QueryAsync(Guid machineId, Guid partId)
    {
        var identity = await fixture.SeedIdentityAsync(machineCode: MachineCode);
        using var client = await fixture.LoginClientAsync(identity);

        var response = await client.GetAsync($"/api/v1/machines/{machineId}/parts/{partId}/compatible-variants");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CompatibleVariantRecord>>()
            ?? throw new InvalidOperationException("Compatible-variants response was empty.");
    }

    private sealed record Scene(
        Guid MachineId,
        Guid V1Id,
        Guid V2Id,
        Guid V3Id,
        Guid MainPartId,
        Guid RelatedPartId,
        Guid AltPartId,
        Guid SharedPartId,
        Guid DuplicatedPartId,
        Guid OrphanPartId);

    private async Task<Scene> SeedSceneAsync()
    {
        await fixture.ResetAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var machineId = await fixture.ScalarAsync<Guid>(
            "SELECT id FROM master.machines WHERE code = @code",
            ("code", MachineCode));

        var v1 = await InsertVariantAsync(machineId, $"P02-{suffix}-V1", 10);
        var v2 = await InsertVariantAsync(machineId, $"P02-{suffix}-V2", 5);
        var v3 = await InsertVariantAsync(machineId, $"P02-{suffix}-V3", 20);

        var mainPart = await InsertPartAsync($"P02-{suffix}-MAIN");
        var relatedPart = await InsertPartAsync($"P02-{suffix}-REL");
        var altPart = await InsertPartAsync($"P02-{suffix}-ALT");
        var sharedPart = await InsertPartAsync($"P02-{suffix}-SHARED");
        var duplicatedPart = await InsertPartAsync($"P02-{suffix}-DUP");
        var orphanPart = await InsertPartAsync($"P02-{suffix}-ORPHAN");
        var pg1SecondMain = await InsertPartAsync($"P02-{suffix}-PG1B-MAIN");

        // V1: one product group carries MAIN plus RELATED/ALT/duplicated children;
        // a second product group in the same variant carries the duplicated part
        // again under a different relation, to prove dedup across group paths.
        var pg1 = await InsertProductGroupAsync(v1, mainPart);
        await InsertGroupPartAsync(pg1, relatedPart, "RELATED");
        await InsertGroupPartAsync(pg1, altPart, "ALT_PART");
        await InsertGroupPartAsync(pg1, duplicatedPart, "RELATED");
        await InsertGroupPartAsync(pg1, sharedPart, "RELATED");

        var pg1B = await InsertProductGroupAsync(v1, pg1SecondMain);
        await InsertGroupPartAsync(pg1B, duplicatedPart, "ALT_PART");

        // V2 and V3 each expose the shared part as their own product group's main part.
        await InsertProductGroupAsync(v2, sharedPart);
        await InsertProductGroupAsync(v3, sharedPart);

        return new Scene(machineId, v1, v2, v3, mainPart, relatedPart, altPart, sharedPart, duplicatedPart, orphanPart);
    }

    private Task<Guid> InsertVariantAsync(Guid machineId, string code, int displayOrder) => fixture.ScalarAsync<Guid>(
        """
        INSERT INTO master.variants (machine_id, code, name, display_order)
        VALUES (@machine_id, @code, @code, @display_order)
        RETURNING id
        """,
        ("machine_id", machineId),
        ("code", code),
        ("display_order", displayOrder));

    private Task<Guid> InsertPartAsync(string code) => fixture.ScalarAsync<Guid>(
        "INSERT INTO master.parts (part_code) VALUES (@code) RETURNING id",
        ("code", code));

    private Task<Guid> InsertProductGroupAsync(Guid variantId, Guid mainPartId) => fixture.ScalarAsync<Guid>(
        """
        INSERT INTO master.product_groups (variant_id, main_part_id)
        VALUES (@variant_id, @main_part_id)
        RETURNING id
        """,
        ("variant_id", variantId),
        ("main_part_id", mainPartId));

    private Task InsertGroupPartAsync(Guid productGroupId, Guid partId, string relationType) => fixture.ExecuteAsync(
        """
        INSERT INTO master.product_group_parts (product_group_id, part_id, relation_type)
        VALUES (@product_group_id, @part_id, @relation_type)
        """,
        ("product_group_id", productGroupId),
        ("part_id", partId),
        ("relation_type", relationType));
}
