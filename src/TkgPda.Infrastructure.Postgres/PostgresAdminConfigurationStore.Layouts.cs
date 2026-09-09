using System.Data;
using Npgsql;
using NpgsqlTypes;
using TkgPda.Application;

namespace TkgPda.Infrastructure.Postgres;

public sealed partial class PostgresAdminConfigurationStore
{
    public async Task<AdminVariantRecord> CreateVariantAsync(
        CreateVariantCommand command,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        AdminVariantRecord created;
        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO master.variants (machine_id, code, name, display_order)
                         SELECT id, @code, @name, @display_order FROM master.machines WHERE id = @machine_id AND is_active
                         RETURNING id, machine_id, code, name, display_order, is_active, NULL::uuid, (xmin::text)::bigint
                         """,
                         connection,
                         transaction))
        {
            insert.Parameters.AddWithValue("machine_id", command.MachineId);
            insert.Parameters.AddWithValue("code", command.Code);
            insert.Parameters.AddWithValue("name", command.Name);
            insert.Parameters.AddWithValue("display_order", command.DisplayOrder);
            await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw NotFound("MACHINE_NOT_FOUND", "The active machine does not exist.");
            }

            created = ReadVariant(reader);
        }

        await InsertAuditAsync(connection, transaction, context, "ADMIN_VARIANT_CREATED", "VARIANT", created.Id.ToString(), null, created, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<AdminVariantRecord> UpdateVariantAsync(
        Guid variantId,
        UpdateVariantCommand command,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await ReadVariantAsync(connection, transaction, variantId, cancellationToken);
        if (!command.IsActive)
        {
            if (await HasActiveTransportAsync(connection, transaction, "variant_id", variantId, cancellationToken))
            {
                throw Conflict("CONFIGURATION_IN_USE", "The variant cannot be archived while transport requests are active.");
            }

            await using var runtime = new NpgsqlCommand("SELECT 1 FROM ops.machine_runtime_state WHERE active_variant_id = @id LIMIT 1", connection, transaction);
            runtime.Parameters.AddWithValue("id", variantId);
            if (await runtime.ExecuteScalarAsync(cancellationToken) is not null)
            {
                throw Conflict("CONFIGURATION_IN_USE", "The active runtime variant must be switched before it can be archived.");
            }
        }

        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE master.variants
                         SET name = @name, display_order = @display_order, is_active = @is_active, updated_at = now()
                         WHERE id = @id AND (xmin::text)::bigint = @expected
                         """,
                         connection,
                         transaction))
        {
            update.Parameters.AddWithValue("id", variantId);
            update.Parameters.AddWithValue("name", command.Name);
            update.Parameters.AddWithValue("display_order", command.DisplayOrder);
            update.Parameters.AddWithValue("is_active", command.IsActive);
            update.Parameters.AddWithValue("expected", command.ExpectedVersion);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw Conflict("CONCURRENCY_CONFLICT", "The variant was changed by another administrator.");
            }
        }

        var after = await ReadVariantAsync(connection, transaction, variantId, cancellationToken);
        await InsertAuditAsync(connection, transaction, context, command.IsActive ? "ADMIN_VARIANT_UPDATED" : "ADMIN_VARIANT_ARCHIVED", "VARIANT", variantId.ToString(), before, after, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return after;
    }

    public async Task<AdminVariantRecord> SetDefaultLayoutAsync(
        Guid variantId,
        Guid layoutId,
        long expectedVersion,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await ReadVariantAsync(connection, transaction, variantId, cancellationToken);
        await using (var touch = new NpgsqlCommand(
                         "UPDATE master.variants SET updated_at = now() WHERE id = @id AND (xmin::text)::bigint = @expected",
                         connection,
                         transaction))
        {
            touch.Parameters.AddWithValue("id", variantId);
            touch.Parameters.AddWithValue("expected", expectedVersion);
            if (await touch.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw Conflict("CONCURRENCY_CONFLICT", "The variant configuration was changed by another administrator.");
            }
        }

        Guid machineId;
        await using (var validate = new NpgsqlCommand(
                         """
                         SELECT v.machine_id
                         FROM master.variants v
                         JOIN master.layouts l ON l.id = @layout_id AND l.machine_id = v.machine_id
                         WHERE v.id = @variant_id AND v.is_active AND l.status = 'PUBLISHED'
                         """,
                         connection,
                         transaction))
        {
            validate.Parameters.AddWithValue("variant_id", variantId);
            validate.Parameters.AddWithValue("layout_id", layoutId);
            machineId = await validate.ExecuteScalarAsync(cancellationToken) is Guid id
                ? id
                : throw Conflict("VARIANT_LAYOUT_CONFLICT", "The default layout must be a published layout of the same machine.");
        }

        await using (var clear = new NpgsqlCommand(
                         "UPDATE master.variant_layout_assignments SET is_default = false WHERE variant_id = @variant_id AND is_default",
                         connection,
                         transaction))
        {
            clear.Parameters.AddWithValue("variant_id", variantId);
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var upsert = new NpgsqlCommand(
                         """
                         INSERT INTO master.variant_layout_assignments (variant_id, machine_id, layout_id, is_default)
                         VALUES (@variant_id, @machine_id, @layout_id, true)
                         ON CONFLICT (variant_id, layout_id) DO UPDATE SET is_default = true, assigned_at = now()
                         """,
                         connection,
                         transaction))
        {
            upsert.Parameters.AddWithValue("variant_id", variantId);
            upsert.Parameters.AddWithValue("machine_id", machineId);
            upsert.Parameters.AddWithValue("layout_id", layoutId);
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        var after = await ReadVariantAsync(connection, transaction, variantId, cancellationToken);
        await InsertAuditAsync(connection, transaction, context, "ADMIN_VARIANT_DEFAULT_LAYOUT_CHANGED", "VARIANT", variantId.ToString(), before, after, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return after;
    }

    public async Task<AdminLayoutRecord> CreateLayoutAsync(
        CreateLayoutCommand command,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var existing = new NpgsqlCommand(
                         "SELECT 1 FROM master.layouts WHERE machine_id = @machine_id AND code = @code AND status = 'DRAFT' LIMIT 1",
                         connection,
                         transaction))
        {
            existing.Parameters.AddWithValue("machine_id", command.MachineId);
            existing.Parameters.AddWithValue("code", command.Code);
            if (await existing.ExecuteScalarAsync(cancellationToken) is not null)
            {
                throw Conflict("DRAFT_LAYOUT_ALREADY_EXISTS", "Only one draft layout version may exist for a machine/code at a time.");
            }
        }

        Guid id;
        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO master.layouts (machine_id, code, name, version, status, created_by)
                         SELECT m.id, @code, @name,
                                COALESCE((SELECT max(version) + 1 FROM master.layouts WHERE machine_id = m.id AND code = @code), 1),
                                'DRAFT', @created_by
                         FROM master.machines m WHERE m.id = @machine_id AND m.is_active
                         RETURNING id
                         """,
                         connection,
                         transaction))
        {
            insert.Parameters.AddWithValue("machine_id", command.MachineId);
            insert.Parameters.AddWithValue("code", command.Code);
            insert.Parameters.AddWithValue("name", command.Name);
            insert.Parameters.AddWithValue("created_by", context.ActorUserId);
            id = await insert.ExecuteScalarAsync(cancellationToken) is Guid value
                ? value
                : throw NotFound("MACHINE_NOT_FOUND", "The active machine does not exist.");
        }

        var created = await ReadLayoutAsync(connection, transaction, id, cancellationToken);
        await InsertAuditAsync(connection, transaction, context, "ADMIN_LAYOUT_DRAFT_CREATED", "LAYOUT", id.ToString(), null, created, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<AdminLayoutRecord> CreateLayoutVersionAsync(
        Guid sourceLayoutId,
        long expectedVersion,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var source = await LockLayoutAsync(connection, transaction, sourceLayoutId, expectedVersion, cancellationToken);
        await using (var existing = new NpgsqlCommand(
                         "SELECT 1 FROM master.layouts WHERE machine_id = @machine_id AND code = @code AND status = 'DRAFT' AND id <> @id LIMIT 1",
                         connection,
                         transaction))
        {
            existing.Parameters.AddWithValue("machine_id", source.MachineId);
            existing.Parameters.AddWithValue("code", source.Code);
            existing.Parameters.AddWithValue("id", sourceLayoutId);
            if (await existing.ExecuteScalarAsync(cancellationToken) is not null || source.Status == "DRAFT")
            {
                throw Conflict("DRAFT_LAYOUT_ALREADY_EXISTS", "A new version cannot be cloned while a draft already exists.");
            }
        }

        var newId = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO master.layouts (id, machine_id, code, name, version, status, created_by)
                         SELECT @new_id, machine_id, code, name,
                                (SELECT max(version) + 1 FROM master.layouts versions WHERE versions.machine_id = source.machine_id AND versions.code = source.code),
                                'DRAFT', @created_by
                         FROM master.layouts source WHERE id = @source_id
                         """,
                         connection,
                         transaction))
        {
            insert.Parameters.AddWithValue("new_id", newId);
            insert.Parameters.AddWithValue("source_id", sourceLayoutId);
            insert.Parameters.AddWithValue("created_by", context.ActorUserId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var copy = new NpgsqlCommand(
                         """
                         INSERT INTO master.layout_slots (
                             layout_id, code, display_name, slot_role, default_container_type_id,
                             ui_x, ui_y, ui_width, ui_height, display_order,
                             allows_empty_load, allows_full_load, is_enabled, metadata_json)
                         SELECT @new_id, code, display_name, slot_role, default_container_type_id,
                                ui_x, ui_y, ui_width, ui_height, display_order,
                                allows_empty_load, allows_full_load, is_enabled, metadata_json
                         FROM master.layout_slots WHERE layout_id = @source_id
                         """,
                         connection,
                         transaction))
        {
            copy.Parameters.AddWithValue("new_id", newId);
            copy.Parameters.AddWithValue("source_id", sourceLayoutId);
            await copy.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var copyParts = new NpgsqlCommand(
                         """
                         INSERT INTO master.layout_slot_parts (layout_slot_id, part_id, created_at)
                         SELECT new_slot.id, lsp.part_id, now()
                         FROM master.layout_slots old_slot
                         JOIN master.layout_slot_parts lsp ON lsp.layout_slot_id = old_slot.id
                         JOIN master.layout_slots new_slot ON new_slot.layout_id = @new_id AND new_slot.code = old_slot.code
                         WHERE old_slot.layout_id = @source_id
                         """,
                         connection,
                         transaction))
        {
            copyParts.Parameters.AddWithValue("new_id", newId);
            copyParts.Parameters.AddWithValue("source_id", sourceLayoutId);
            var copiedCount = await copyParts.ExecuteNonQueryAsync(cancellationToken);

            await using var verifyCmd = new NpgsqlCommand(
                """
                SELECT count(*)
                FROM master.layout_slots old_slot
                JOIN master.layout_slot_parts lsp ON lsp.layout_slot_id = old_slot.id
                WHERE old_slot.layout_id = @source_id
                """,
                connection,
                transaction);
            verifyCmd.Parameters.AddWithValue("source_id", sourceLayoutId);
            var expectedCount = Convert.ToInt64(await verifyCmd.ExecuteScalarAsync(cancellationToken));

            if (copiedCount != expectedCount)
            {
                throw new InvalidOperationException($"Relational loss during layout version cloning: expected {expectedCount} layout_slot_parts, copied {copiedCount}.");
            }
        }

        var created = await ReadLayoutAsync(connection, transaction, newId, cancellationToken);
        await InsertAuditAsync(connection, transaction, context, "ADMIN_LAYOUT_VERSION_CREATED", "LAYOUT", newId.ToString(), new { sourceLayoutId, source.Version }, created, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<AdminLayoutRecord> UpdateLayoutAsync(
        Guid layoutId,
        UpdateLayoutCommand command,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await LockLayoutAsync(connection, transaction, layoutId, command.ExpectedVersion, cancellationToken);
        if (before.Status != "DRAFT")
        {
            throw Conflict("LAYOUT_NOT_DRAFT", "Published and archived layouts are immutable; create a new version.");
        }

        await using (var update = new NpgsqlCommand("UPDATE master.layouts SET name = @name WHERE id = @id", connection, transaction))
        {
            update.Parameters.AddWithValue("id", layoutId);
            update.Parameters.AddWithValue("name", command.Name);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        var after = await ReadLayoutAsync(connection, transaction, layoutId, cancellationToken);
        await InsertAuditAsync(connection, transaction, context, "ADMIN_LAYOUT_DRAFT_UPDATED", "LAYOUT", layoutId.ToString(), before, after, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return after;
    }

    public async Task<AdminLayoutRecord> PublishLayoutAsync(
        Guid layoutId,
        long expectedVersion,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await LockLayoutAsync(connection, transaction, layoutId, expectedVersion, cancellationToken);
        if (before.Status != "DRAFT")
        {
            throw Conflict("LAYOUT_NOT_DRAFT", "Only a draft layout can be published.");
        }

        await ValidateLayoutForPublicationAsync(connection, transaction, layoutId, cancellationToken);
        Guid? previousPublishedId = null;
        await using (var previous = new NpgsqlCommand(
                         """
                         SELECT id FROM master.layouts
                         WHERE machine_id = @machine_id AND code = @code AND status = 'PUBLISHED' AND id <> @id
                         FOR UPDATE
                         """,
                         connection,
                         transaction))
        {
            previous.Parameters.AddWithValue("machine_id", before.MachineId);
            previous.Parameters.AddWithValue("code", before.Code);
            previous.Parameters.AddWithValue("id", layoutId);
            previousPublishedId = await previous.ExecuteScalarAsync(cancellationToken) as Guid?;
        }

        if (previousPublishedId.HasValue)
        {
            if (await HasActiveTransportAsync(connection, transaction, "layout_id", previousPublishedId.Value, cancellationToken))
            {
                throw Conflict("CONFIGURATION_IN_USE", "A replacement layout cannot be published while the current version has active transports.");
            }

            await using (var archive = new NpgsqlCommand(
                             "UPDATE master.layouts SET status = 'ARCHIVED' WHERE id = @id",
                             connection,
                             transaction))
            {
                archive.Parameters.AddWithValue("id", previousPublishedId.Value);
                await archive.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var migrate = new NpgsqlCommand(
                             "UPDATE master.variant_layout_assignments SET layout_id = @new_id, assigned_at = now() WHERE layout_id = @old_id",
                             connection,
                             transaction))
            {
                migrate.Parameters.AddWithValue("new_id", layoutId);
                migrate.Parameters.AddWithValue("old_id", previousPublishedId.Value);
                await migrate.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var publish = new NpgsqlCommand(
                         "UPDATE master.layouts SET status = 'PUBLISHED', published_at = now() WHERE id = @id",
                         connection,
                         transaction))
        {
            publish.Parameters.AddWithValue("id", layoutId);
            await publish.ExecuteNonQueryAsync(cancellationToken);
        }

        var after = await ReadLayoutAsync(connection, transaction, layoutId, cancellationToken);
        await InsertAuditAsync(connection, transaction, context, "ADMIN_LAYOUT_PUBLISHED", "LAYOUT", layoutId.ToString(), before, new { layout = after, replacedLayoutId = previousPublishedId }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return after;
    }

    public async Task<AdminLayoutRecord> ArchiveLayoutAsync(
        Guid layoutId,
        long expectedVersion,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await LockLayoutAsync(connection, transaction, layoutId, expectedVersion, cancellationToken);
        if (before.Status == "ARCHIVED")
        {
            return before;
        }

        if (await HasActiveTransportAsync(connection, transaction, "layout_id", layoutId, cancellationToken))
        {
            throw Conflict("CONFIGURATION_IN_USE", "The layout cannot be archived while transport requests are active.");
        }

        await using (var runtime = new NpgsqlCommand("SELECT 1 FROM ops.machine_runtime_state WHERE active_layout_id = @id LIMIT 1", connection, transaction))
        {
            runtime.Parameters.AddWithValue("id", layoutId);
            if (await runtime.ExecuteScalarAsync(cancellationToken) is not null)
            {
                throw Conflict("CONFIGURATION_IN_USE", "The active runtime layout must be switched before it can be archived.");
            }
        }

        await using (var assignments = new NpgsqlCommand("DELETE FROM master.variant_layout_assignments WHERE layout_id = @id", connection, transaction))
        {
            assignments.Parameters.AddWithValue("id", layoutId);
            await assignments.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var archive = new NpgsqlCommand("UPDATE master.layouts SET status = 'ARCHIVED' WHERE id = @id", connection, transaction))
        {
            archive.Parameters.AddWithValue("id", layoutId);
            await archive.ExecuteNonQueryAsync(cancellationToken);
        }

        var after = await ReadLayoutAsync(connection, transaction, layoutId, cancellationToken);
        await InsertAuditAsync(connection, transaction, context, "ADMIN_LAYOUT_ARCHIVED", "LAYOUT", layoutId.ToString(), before, after, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return after;
    }

    public async Task<AdminSlotRecord> CreateSlotAsync(
        Guid layoutId,
        CreateSlotCommand command,
        long expectedLayoutVersion,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        _ = await EnsureDraftLayoutAsync(connection, transaction, layoutId, expectedLayoutVersion, cancellationToken);
        AdminSlotRecord created;
        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO master.layout_slots (
                             layout_id, code, display_name, slot_role, default_container_type_id,
                             ui_x, ui_y, ui_width, ui_height, display_order,
                             allows_empty_load, allows_full_load, metadata_json)
                         VALUES (@layout_id, @code, @display_name, @role, @container_id,
                                 @x, @y, @width, @height, @display_order,
                                 @allows_empty, @allows_full, @metadata)
                         RETURNING id, layout_id, code, display_name, slot_role, default_container_type_id,
                                   ui_x, ui_y, ui_width, ui_height, display_order,
                                   allows_empty_load, allows_full_load, is_enabled,
                                   metadata_json::text, (xmin::text)::bigint
                         """,
                         connection,
                         transaction))
        {
            AddSlotParameters(insert, layoutId, command.Code, command.DisplayName, command.Role, command.DefaultContainerTypeId,
                command.X, command.Y, command.Width, command.Height, command.DisplayOrder, command.AllowsEmptyLoad, command.AllowsFullLoad, command.Metadata.GetRawText());
            await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            created = ReadSlot(reader);
        }

        await InsertAuditAsync(connection, transaction, context, "ADMIN_LAYOUT_SLOT_CREATED", "LAYOUT_SLOT", created.Id.ToString(), null, created, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<AdminSlotRecord> UpdateSlotAsync(
        Guid layoutId,
        Guid slotId,
        UpdateSlotCommand command,
        AdminMutationContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        _ = await EnsureDraftLayoutAsync(connection, transaction, layoutId, command.ExpectedLayoutVersion, cancellationToken);
        var before = await ReadSlotAsync(connection, transaction, layoutId, slotId, cancellationToken);
        AdminSlotRecord after;
        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE master.layout_slots
                         SET display_name = @display_name, slot_role = @role, default_container_type_id = @container_id,
                             ui_x = @x, ui_y = @y, ui_width = @width, ui_height = @height,
                             display_order = @display_order, allows_empty_load = @allows_empty,
                             allows_full_load = @allows_full, is_enabled = @is_enabled,
                             metadata_json = @metadata, updated_at = now()
                         WHERE id = @id AND layout_id = @layout_id AND (xmin::text)::bigint = @expected
                         RETURNING id, layout_id, code, display_name, slot_role, default_container_type_id,
                                   ui_x, ui_y, ui_width, ui_height, display_order,
                                   allows_empty_load, allows_full_load, is_enabled,
                                   metadata_json::text, (xmin::text)::bigint
                         """,
                         connection,
                         transaction))
        {
            update.Parameters.AddWithValue("id", slotId);
            AddSlotParameters(update, layoutId, before.Code, command.DisplayName, command.Role, command.DefaultContainerTypeId,
                command.X, command.Y, command.Width, command.Height, command.DisplayOrder, command.AllowsEmptyLoad, command.AllowsFullLoad, command.Metadata.GetRawText());
            update.Parameters.AddWithValue("is_enabled", command.IsEnabled);
            update.Parameters.AddWithValue("expected", command.ExpectedVersion);
            await using var reader = await update.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw Conflict("CONCURRENCY_CONFLICT", "The slot was changed by another administrator.");
            }

            after = ReadSlot(reader);
        }

        await InsertAuditAsync(connection, transaction, context, command.IsEnabled ? "ADMIN_LAYOUT_SLOT_UPDATED" : "ADMIN_LAYOUT_SLOT_ARCHIVED", "LAYOUT_SLOT", slotId.ToString(), before, after, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return after;
    }

    private static async Task<AdminVariantRecord> ReadVariantAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT v.id, v.machine_id, v.code, v.name, v.display_order, v.is_active,
                   (SELECT a.layout_id FROM master.variant_layout_assignments a WHERE a.variant_id = v.id AND a.is_default),
                   (v.xmin::text)::bigint
            FROM master.variants v WHERE v.id = @id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadVariant(reader) : throw NotFound("VARIANT_NOT_FOUND", "The variant does not exist.");
    }

    private static async Task<AdminLayoutRecord> ReadLayoutAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(LayoutSelectSql + " WHERE l.id = @id", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadLayout(reader) : throw NotFound("LAYOUT_NOT_FOUND", "The layout does not exist.");
    }

    private static async Task<AdminLayoutRecord> LockLayoutAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid id,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(LayoutSelectSql + " WHERE l.id = @id AND (l.xmin::text)::bigint = @expected FOR UPDATE OF l", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("expected", expectedVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadLayout(reader);
        }

        await reader.DisposeAsync();
        await using var exists = new NpgsqlCommand("SELECT 1 FROM master.layouts WHERE id = @id", connection, transaction);
        exists.Parameters.AddWithValue("id", id);
        return await exists.ExecuteScalarAsync(cancellationToken) is null
            ? throw NotFound("LAYOUT_NOT_FOUND", "The layout does not exist.")
            : throw Conflict("CONCURRENCY_CONFLICT", "The layout was changed by another administrator.");
    }

    private static async Task<AdminLayoutRecord> EnsureDraftLayoutAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid id,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var layout = await LockLayoutAsync(connection, transaction, id, expectedVersion, cancellationToken);
        return layout.Status == "DRAFT"
            ? layout
            : throw Conflict("LAYOUT_NOT_DRAFT", "Slots can be changed only on a draft layout version.");
    }

    private static async Task<AdminSlotRecord> ReadSlotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid layoutId,
        Guid slotId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SlotSelectSql + " WHERE s.id = @id AND s.layout_id = @layout_id", connection, transaction);
        command.Parameters.AddWithValue("id", slotId);
        command.Parameters.AddWithValue("layout_id", layoutId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSlot(reader) : throw NotFound("SLOT_NOT_FOUND", "The layout slot does not exist.");
    }

    private static async Task ValidateLayoutForPublicationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid layoutId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT
                count(*) FILTER (WHERE is_enabled AND slot_role <> 'MACHINE')::int,
                count(*) FILTER (
                    WHERE is_enabled AND (
                        ui_x IS NULL OR ui_y IS NULL OR ui_width IS NULL OR ui_height IS NULL OR
                        ui_x < 0 OR ui_y < 0 OR ui_width <= 0 OR ui_height <= 0 OR
                        ui_x + ui_width > 1 OR ui_y + ui_height > 1))::int
            FROM master.layout_slots WHERE layout_id = @id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", layoutId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        if (reader.GetInt32(0) == 0)
        {
            throw Conflict("LAYOUT_HAS_NO_TRANSPORT_SLOTS", "A published layout requires at least one enabled non-MACHINE slot.");
        }

        if (reader.GetInt32(1) != 0)
        {
            throw Conflict("INVALID_SLOT_GEOMETRY", "Every enabled slot must have complete normalized display geometry before publication.");
        }
    }

    private static void AddSlotParameters(
        NpgsqlCommand command,
        Guid layoutId,
        string code,
        string displayName,
        string role,
        Guid? containerId,
        decimal? x,
        decimal? y,
        decimal? width,
        decimal? height,
        int displayOrder,
        bool allowsEmpty,
        bool allowsFull,
        string metadata)
    {
        command.Parameters.AddWithValue("layout_id", layoutId);
        command.Parameters.AddWithValue("code", code);
        command.Parameters.AddWithValue("display_name", displayName);
        command.Parameters.AddWithValue("role", role);
        AddNullableUuid(command, "container_id", containerId);
        AddNullableNumeric(command, "x", x);
        AddNullableNumeric(command, "y", y);
        AddNullableNumeric(command, "width", width);
        AddNullableNumeric(command, "height", height);
        command.Parameters.AddWithValue("display_order", displayOrder);
        command.Parameters.AddWithValue("allows_empty", allowsEmpty);
        command.Parameters.AddWithValue("allows_full", allowsFull);
        command.Parameters.Add("metadata", NpgsqlDbType.Jsonb).Value = metadata;
    }

    private static void AddNullableNumeric(NpgsqlCommand command, string name, decimal? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Numeric).Value = value.HasValue ? value.Value : DBNull.Value;
}
