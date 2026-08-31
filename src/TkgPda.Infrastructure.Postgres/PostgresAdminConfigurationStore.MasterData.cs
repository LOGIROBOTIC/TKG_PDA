using System.Data;
using Npgsql;
using NpgsqlTypes;
using TkgPda.Application;

namespace TkgPda.Infrastructure.Postgres;

public sealed partial class PostgresAdminConfigurationStore
{
    public async Task<AdminPartRecord> CreatePartAsync(
        CreatePartCommand command,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        AdminPartRecord created;
        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO master.parts (part_code, description)
                         VALUES (@code, @description)
                         RETURNING id, part_code, description, is_active, (xmin::text)::bigint
                         """,
                         connection,
                         transaction))
        {
            insert.Parameters.AddWithValue("code", command.Code);
            AddNullableText(insert, "description", command.Description);
            await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            created = ReadPart(reader);
        }

        await InsertAuditAsync(connection, transaction, context, "ADMIN_PART_CREATED", "PART", created.Id.ToString(), null, created, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<AdminPartRecord> UpdatePartAsync(
        Guid partId,
        UpdatePartCommand command,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await ReadPartAsync(connection, transaction, partId, cancellationToken);
        if (!command.IsActive)
        {
            if (await HasActiveTransportAsync(connection, transaction, "part_id", partId, cancellationToken)
                || await ExistsAsync(connection, transaction, "SELECT 1 FROM ops.slot_runtime_state WHERE current_part_id = @id LIMIT 1", partId, cancellationToken))
            {
                throw Conflict("CONFIGURATION_IN_USE", "The part is present in active workflow/runtime state and cannot be archived.");
            }
        }

        AdminPartRecord after;
        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE master.parts SET description = @description, is_active = @is_active, updated_at = now()
                         WHERE id = @id AND (xmin::text)::bigint = @expected
                         RETURNING id, part_code, description, is_active, (xmin::text)::bigint
                         """,
                         connection,
                         transaction))
        {
            update.Parameters.AddWithValue("id", partId);
            AddNullableText(update, "description", command.Description);
            update.Parameters.AddWithValue("is_active", command.IsActive);
            update.Parameters.AddWithValue("expected", command.ExpectedVersion);
            await using var reader = await update.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw Conflict("CONCURRENCY_CONFLICT", "The part was changed by another administrator.");
            }

            after = ReadPart(reader);
        }

        await InsertAuditAsync(connection, transaction, context, command.IsActive ? "ADMIN_PART_UPDATED" : "ADMIN_PART_ARCHIVED", "PART", partId.ToString(), before, after, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return after;
    }

    // Customer UX — Automatic Machine Slot Resolution: curated
    // master.layout_slot_parts CRUD. No concurrency token — this is a pure
    // membership set (like the RDS slot mapping), not a versioned resource.
    public async Task<IReadOnlyList<AdminSlotPartRecord>> ListSlotPartsAsync(Guid slotId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT p.id, p.part_code, p.description
            FROM master.layout_slot_parts lsp
            JOIN master.parts p ON p.id = lsp.part_id
            WHERE lsp.layout_slot_id = @slot_id
            ORDER BY p.part_code
            """,
            connection);
        command.Parameters.AddWithValue("slot_id", slotId);
        var results = new List<AdminSlotPartRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new AdminSlotPartRecord(reader.GetGuid(0), reader.GetString(1), NullableString(reader, 2)));
        }

        return results;
    }

    public async Task AssignSlotPartAsync(Guid slotId, Guid partId, AdminMutationContext context, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await ExistsAsync(connection, transaction, "SELECT 1 FROM master.layout_slots WHERE id = @id", slotId, cancellationToken))
        {
            throw NotFound("LAYOUT_SLOT_NOT_FOUND", "The layout slot does not exist.");
        }

        if (!await ExistsAsync(connection, transaction, "SELECT 1 FROM master.parts WHERE id = @id AND is_active", partId, cancellationToken))
        {
            throw NotFound("PART_NOT_FOUND", "The active part does not exist.");
        }

        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO master.layout_slot_parts (layout_slot_id, part_id)
                         VALUES (@slot_id, @part_id)
                         ON CONFLICT (layout_slot_id, part_id) DO NOTHING
                         """,
                         connection,
                         transaction))
        {
            insert.Parameters.AddWithValue("slot_id", slotId);
            insert.Parameters.AddWithValue("part_id", partId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertAuditAsync(
            connection, transaction, context, "ADMIN_LAYOUT_SLOT_PART_ASSIGNED", "LAYOUT_SLOT", slotId.ToString(),
            null, new { slotId, partId }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveSlotPartAsync(Guid slotId, Guid partId, AdminMutationContext context, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var delete = new NpgsqlCommand(
                         "DELETE FROM master.layout_slot_parts WHERE layout_slot_id = @slot_id AND part_id = @part_id",
                         connection,
                         transaction))
        {
            delete.Parameters.AddWithValue("slot_id", slotId);
            delete.Parameters.AddWithValue("part_id", partId);
            if (await delete.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw NotFound("LAYOUT_SLOT_PART_NOT_FOUND", "The slot-part assignment does not exist.");
            }
        }

        await InsertAuditAsync(
            connection, transaction, context, "ADMIN_LAYOUT_SLOT_PART_REMOVED", "LAYOUT_SLOT", slotId.ToString(),
            new { slotId, partId }, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<AdminProductGroupRecord> CreateProductGroupAsync(
        CreateProductGroupCommand command,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        Guid id;
        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO master.product_groups (variant_id, main_part_id, name, display_order)
                         SELECT v.id, p.id, @name, @display_order
                         FROM master.variants v CROSS JOIN master.parts p
                         WHERE v.id = @variant_id AND v.is_active AND p.id = @part_id AND p.is_active
                         RETURNING id
                         """,
                         connection,
                         transaction))
        {
            insert.Parameters.AddWithValue("variant_id", command.VariantId);
            insert.Parameters.AddWithValue("part_id", command.MainPartId);
            AddNullableText(insert, "name", command.Name);
            insert.Parameters.AddWithValue("display_order", command.DisplayOrder);
            id = await insert.ExecuteScalarAsync(cancellationToken) is Guid value
                ? value
                : throw NotFound("VARIANT_OR_PART_NOT_FOUND", "The active variant and main part are required.");
        }

        var created = await ReadProductGroupAsync(connection, transaction, id, cancellationToken);
        await InsertAuditAsync(connection, transaction, context, "ADMIN_PRODUCT_GROUP_CREATED", "PRODUCT_GROUP", id.ToString(), null, created, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<AdminProductGroupRecord> UpdateProductGroupAsync(
        Guid groupId,
        UpdateProductGroupCommand command,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await ReadProductGroupAsync(connection, transaction, groupId, cancellationToken);
        if (!command.IsActive && await HasActiveTransportAsync(connection, transaction, "product_group_id", groupId, cancellationToken))
        {
            throw Conflict("CONFIGURATION_IN_USE", "The product group cannot be archived while transport requests are active.");
        }

        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE master.product_groups
                         SET name = @name, display_order = @display_order, is_active = @is_active, updated_at = now()
                         WHERE id = @id AND (xmin::text)::bigint = @expected
                         """,
                         connection,
                         transaction))
        {
            update.Parameters.AddWithValue("id", groupId);
            AddNullableText(update, "name", command.Name);
            update.Parameters.AddWithValue("display_order", command.DisplayOrder);
            update.Parameters.AddWithValue("is_active", command.IsActive);
            update.Parameters.AddWithValue("expected", command.ExpectedVersion);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw Conflict("CONCURRENCY_CONFLICT", "The product group was changed by another administrator.");
            }
        }

        var after = await ReadProductGroupAsync(connection, transaction, groupId, cancellationToken);
        await InsertAuditAsync(connection, transaction, context, command.IsActive ? "ADMIN_PRODUCT_GROUP_UPDATED" : "ADMIN_PRODUCT_GROUP_ARCHIVED", "PRODUCT_GROUP", groupId.ToString(), before, after, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return after;
    }

    public async Task<AdminProductGroupRecord> SetProductGroupPartAsync(
        Guid groupId,
        Guid partId,
        SetProductGroupPartCommand command,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await ReadProductGroupAsync(connection, transaction, groupId, cancellationToken);
        await TouchProductGroupAsync(connection, transaction, groupId, command.ExpectedGroupVersion, cancellationToken);
        await using (var validate = new NpgsqlCommand(
                         """
                         SELECT 1
                         FROM master.parts p
                         WHERE p.id = @part_id AND p.is_active
                           AND (@container_id IS NULL OR EXISTS (
                               SELECT 1 FROM master.container_types c WHERE c.id = @container_id AND c.is_active))
                         """,
                         connection,
                         transaction))
        {
            validate.Parameters.AddWithValue("part_id", partId);
            AddNullableUuid(validate, "container_id", command.ContainerTypeId);
            if (await validate.ExecuteScalarAsync(cancellationToken) is null)
            {
                throw NotFound("PART_OR_CONTAINER_NOT_FOUND", "The active part and optional active container are required.");
            }
        }

        await using (var upsert = new NpgsqlCommand(
                         """
                         INSERT INTO master.product_group_parts (
                             product_group_id, part_id, container_type_id, relation_type,
                             lot_quantity, display_order, is_active)
                         VALUES (@group_id, @part_id, @container_id, @relation_type,
                                 @lot_quantity, @display_order, @is_active)
                         ON CONFLICT (product_group_id, part_id) DO UPDATE SET
                             container_type_id = EXCLUDED.container_type_id,
                             relation_type = EXCLUDED.relation_type,
                             lot_quantity = EXCLUDED.lot_quantity,
                             display_order = EXCLUDED.display_order,
                             is_active = EXCLUDED.is_active,
                             updated_at = now()
                         """,
                         connection,
                         transaction))
        {
            upsert.Parameters.AddWithValue("group_id", groupId);
            upsert.Parameters.AddWithValue("part_id", partId);
            AddNullableUuid(upsert, "container_id", command.ContainerTypeId);
            upsert.Parameters.AddWithValue("relation_type", command.RelationType);
            AddNullableNumeric(upsert, "lot_quantity", command.LotQuantity);
            upsert.Parameters.AddWithValue("display_order", command.DisplayOrder);
            upsert.Parameters.AddWithValue("is_active", command.IsActive);
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        var after = await ReadProductGroupAsync(connection, transaction, groupId, cancellationToken);
        await InsertAuditAsync(connection, transaction, context, "ADMIN_PRODUCT_GROUP_PART_SET", "PRODUCT_GROUP", groupId.ToString(), before, after, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return after;
    }

    public async Task RemoveProductGroupPartAsync(
        Guid groupId,
        Guid partId,
        long expectedGroupVersion,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await ReadProductGroupAsync(connection, transaction, groupId, cancellationToken);
        await TouchProductGroupAsync(connection, transaction, groupId, expectedGroupVersion, cancellationToken);
        await using (var delete = new NpgsqlCommand(
                         "DELETE FROM master.product_group_parts WHERE product_group_id = @group_id AND part_id = @part_id",
                         connection,
                         transaction))
        {
            delete.Parameters.AddWithValue("group_id", groupId);
            delete.Parameters.AddWithValue("part_id", partId);
            if (await delete.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw NotFound("PRODUCT_GROUP_PART_NOT_FOUND", "The product-group part relation does not exist.");
            }
        }

        var after = await ReadProductGroupAsync(connection, transaction, groupId, cancellationToken);
        await InsertAuditAsync(connection, transaction, context, "ADMIN_PRODUCT_GROUP_PART_REMOVED", "PRODUCT_GROUP", groupId.ToString(), before, after, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<AdminContainerRecord> CreateContainerAsync(
        CreateContainerCommand command,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        AdminContainerRecord created;
        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO master.container_types (
                             code, name, category, length_mm, width_mm, height_mm, capacity_qty)
                         VALUES (@code, @name, @category, @length, @width, @height, @capacity)
                         RETURNING id, code, name, category, length_mm, width_mm, height_mm, capacity_qty, is_active, (xmin::text)::bigint
                         """,
                         connection,
                         transaction))
        {
            AddContainerParameters(insert, command.Code, command.Name, command.Category, command.LengthMm, command.WidthMm, command.HeightMm, command.CapacityQuantity);
            await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            created = ReadContainer(reader);
        }

        await InsertAuditAsync(connection, transaction, context, "ADMIN_CONTAINER_CREATED", "CONTAINER_TYPE", created.Id.ToString(), null, created, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<AdminContainerRecord> UpdateContainerAsync(
        Guid containerId,
        UpdateContainerCommand command,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await ReadContainerAsync(connection, transaction, containerId, cancellationToken);
        if (!command.IsActive)
        {
            if (await HasActiveTransportAsync(connection, transaction, "container_type_id", containerId, cancellationToken)
                || await ExistsAsync(connection, transaction, "SELECT 1 FROM ops.slot_runtime_state WHERE current_container_type_id = @id LIMIT 1", containerId, cancellationToken)
                || await ExistsAsync(
                    connection,
                    transaction,
                    "SELECT 1 FROM master.layout_slots s JOIN master.layouts l ON l.id = s.layout_id WHERE s.default_container_type_id = @id AND s.is_enabled AND l.status = 'PUBLISHED' LIMIT 1",
                    containerId,
                    cancellationToken))
            {
                throw Conflict("CONFIGURATION_IN_USE", "The container is referenced by active workflow/runtime or a published enabled slot.");
            }
        }

        AdminContainerRecord after;
        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE master.container_types
                         SET name = @name, category = @category, length_mm = @length, width_mm = @width,
                             height_mm = @height, capacity_qty = @capacity, is_active = @is_active, updated_at = now()
                         WHERE id = @id AND (xmin::text)::bigint = @expected
                         RETURNING id, code, name, category, length_mm, width_mm, height_mm, capacity_qty, is_active, (xmin::text)::bigint
                         """,
                         connection,
                         transaction))
        {
            update.Parameters.AddWithValue("id", containerId);
            AddContainerParameters(update, before.Code, command.Name, command.Category, command.LengthMm, command.WidthMm, command.HeightMm, command.CapacityQuantity);
            update.Parameters.AddWithValue("is_active", command.IsActive);
            update.Parameters.AddWithValue("expected", command.ExpectedVersion);
            await using var reader = await update.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw Conflict("CONCURRENCY_CONFLICT", "The container was changed by another administrator.");
            }

            after = ReadContainer(reader);
        }

        await InsertAuditAsync(connection, transaction, context, command.IsActive ? "ADMIN_CONTAINER_UPDATED" : "ADMIN_CONTAINER_ARCHIVED", "CONTAINER_TYPE", containerId.ToString(), before, after, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return after;
    }

    private static async Task<AdminPartRecord> ReadPartAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT id, part_code, description, is_active, (xmin::text)::bigint FROM master.parts WHERE id = @id",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPart(reader) : throw NotFound("PART_NOT_FOUND", "The part does not exist.");
    }

    private static async Task<AdminContainerRecord> ReadContainerAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT id, code, name, category, length_mm, width_mm, height_mm, capacity_qty, is_active, (xmin::text)::bigint FROM master.container_types WHERE id = @id",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadContainer(reader) : throw NotFound("CONTAINER_NOT_FOUND", "The container does not exist.");
    }

    private static async Task TouchProductGroupAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid groupId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE master.product_groups SET updated_at = now() WHERE id = @id AND (xmin::text)::bigint = @expected",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", groupId);
        command.Parameters.AddWithValue("expected", expectedVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw Conflict("CONCURRENCY_CONFLICT", "The product group was changed by another administrator.");
        }
    }

    private static async Task<bool> ExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static void AddContainerParameters(
        NpgsqlCommand command,
        string code,
        string name,
        string category,
        int? length,
        int? width,
        int? height,
        decimal? capacity)
    {
        command.Parameters.AddWithValue("code", code);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("category", category);
        AddNullableInteger(command, "length", length);
        AddNullableInteger(command, "width", width);
        AddNullableInteger(command, "height", height);
        AddNullableNumeric(command, "capacity", capacity);
    }

    private static void AddNullableInteger(NpgsqlCommand command, string name, int? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Integer).Value = value.HasValue ? value.Value : DBNull.Value;
}
