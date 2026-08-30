using TkgPda.Domain;

namespace TkgPda.Application;

public sealed record AuthUserRecord(
    Guid Id,
    string Username,
    string PasswordHash,
    string FullName,
    string? EmployeeNo,
    bool IsActive,
    IReadOnlyList<string> Roles);

public sealed record DeviceRecord(
    Guid Id,
    string Code,
    string Name,
    string Type,
    bool IsActive);

public sealed record MachinePermissionRecord(
    Guid MachineId,
    bool CanView,
    bool CanOperate,
    bool CanChangeVariant,
    bool CanManageSlots);

public sealed record MachineSummaryRecord(
    Guid Id,
    string Code,
    string DisplayName,
    int DisplayOrder,
    MachinePermissionRecord Permissions);

/// <summary>
/// WP-02: a variant compatible with a selected part on an authorized machine,
/// derived only from existing normalized master data (never inferred).
/// RelationType is "MAIN" when the part is the product group's main part,
/// otherwise the product_group_parts relation_type (RELATED/ALT_PART/
/// CONSUMABLE/OTHER).
/// </summary>
public sealed record CompatibleVariantRecord(
    Guid VariantId,
    string VariantCode,
    string VariantName,
    Guid MachineId,
    Guid ProductGroupId,
    string RelationType);

/// <summary>
/// WP-03A: a Related/Alt part of a machine-level main part option. RelationType
/// is the product_group_parts.relation_type (RELATED/ALT_PART/CONSUMABLE/OTHER).
/// Any of these part IDs (main or related) is a valid partId for
/// GET /api/v1/machines/{machineId}/parts/{partId}/compatible-variants.
/// </summary>
public sealed record MachineRelatedPartRecord(
    Guid PartId,
    string PartCode,
    string? Description,
    string RelationType);

/// <summary>
/// WP-03A: a machine-level main-part selection option, deduplicated across all
/// of the machine's variants. Derived only from existing master data
/// (master.product_groups / master.product_group_parts); no variant selection
/// is required to see this projection.
/// </summary>
public sealed record MachineMainPartOptionRecord(
    Guid PartId,
    string PartCode,
    string? Description,
    IReadOnlyList<MachineRelatedPartRecord> RelatedParts);

public sealed record VariantLayoutOptionRecord(
    Guid VariantId,
    string VariantCode,
    string VariantName,
    Guid LayoutId,
    string LayoutCode,
    string LayoutName,
    bool IsDefault);

public sealed record RdsWorkSiteProjectionRecord(
    bool IsMapped,
    string? SiteId,
    string? Area,
    string? GroupName,
    int? Type,
    int? Filled,
    int? Locked,
    int? Disabled,
    int? SyncFailed,
    string? Content,
    string? AgvId,
    DateTimeOffset? LastSyncAt);

public sealed record SlotContextDataRecord(
    Guid Id,
    string Code,
    string DisplayName,
    string Role,
    bool IsEnabled,
    bool TransportEligible,
    Guid? DefaultContainerTypeId,
    string? DefaultContainerCode,
    decimal? X,
    decimal? Y,
    decimal? Width,
    decimal? Height,
    int DisplayOrder,
    string OccupancyState,
    string LoadState,
    Guid? CurrentPartId,
    string? CurrentPartCode,
    Guid? CurrentContainerTypeId,
    string? CurrentContainerCode,
    long RowVersion,
    DateTimeOffset? RuntimeUpdatedAt,
    RdsWorkSiteProjectionRecord RdsWorkSite);

public sealed record PdaAvailableActionRecord(
    string Action,
    bool Enabled,
    string? DisabledCode,
    string? DisabledMessage);

public sealed record PdaExecutionReadinessRecord(
    bool RdsWorkSiteMapped,
    bool IsExecutionReady,
    string? ReadinessCode,
    string? ReadinessMessage);

public sealed record SlotContextRecord(
    Guid Id,
    string Code,
    string DisplayName,
    string Role,
    bool IsEnabled,
    bool TransportEligible,
    Guid? DefaultContainerTypeId,
    string? DefaultContainerCode,
    decimal? X,
    decimal? Y,
    decimal? Width,
    decimal? Height,
    int DisplayOrder,
    string OccupancyState,
    string LoadState,
    Guid? CurrentPartId,
    string? CurrentPartCode,
    Guid? CurrentContainerTypeId,
    string? CurrentContainerCode,
    long RowVersion,
    DateTimeOffset? RuntimeUpdatedAt,
    RdsWorkSiteProjectionRecord RdsWorkSite,
    PdaExecutionReadinessRecord ExecutionReadiness,
    IReadOnlyList<PdaAvailableActionRecord> AvailableActions,
    IReadOnlyList<PdaAvailableActionRecord> LoadStateActions);

public sealed record PdaProductGroupPartRecord(
    Guid Id,
    Guid PartId,
    string PartCode,
    Guid? ContainerTypeId,
    string? ContainerCode,
    string RelationType,
    decimal? LotQuantity,
    int DisplayOrder);

public sealed record PdaProductGroupRecord(
    Guid Id,
    Guid MainPartId,
    string MainPartCode,
    string? Name,
    int DisplayOrder,
    IReadOnlyList<PdaProductGroupPartRecord> Parts);

public sealed record PdaTaskTemplateRecord(
    Guid Id,
    Guid VariantId,
    Guid? ProductGroupId,
    Guid TaskTypeId,
    string Operation,
    Guid? SourceSlotGroupId,
    Guid? DestinationSlotGroupId,
    string? RdsTaskLabel,
    bool RdsTaskLabelConfigured,
    int Priority);

public sealed record RdsTaskDataRecord(
    string? TaskRecordId,
    string? TaskLabel,
    string? OutOrderNo,
    string? AgvId,
    int? Status,
    string? EndedReason,
    DateTimeOffset? SentAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset? LastSyncAt);

public sealed record ActiveTransportDataRecord(
    Guid RequestId,
    long RequestNo,
    TransportTaskType Operation,
    TransportRequestStatus Status,
    Guid? RequestedBy,
    string? RequestedByUsername,
    bool IsMine,
    Guid? SourceSlotId,
    Guid? DestinationSlotId,
    Guid? ProductGroupId,
    Guid? PartId,
    Guid? ContainerTypeId,
    int Priority,
    DateTimeOffset CreatedAt,
    string? FailureCode,
    string? FailureMessage,
    Guid CorrelationId,
    RdsTaskDataRecord? RdsTask);

public sealed record PdaExecutionProjectionRecord(
    string State,
    string? RdsTaskRecordId,
    string? RdsTaskLabel,
    string? OutOrderNo,
    string? AgvId,
    int? RdsStatus,
    DateTimeOffset? LastSyncAt,
    string? FailureCode,
    string? FailureCategory,
    string? OperatorMessage,
    string? TechnicalMessage,
    Guid CorrelationId);

public sealed record ActiveTransportRequestRecord(
    Guid RequestId,
    long RequestNo,
    string Operation,
    string Status,
    Guid? RequestedBy,
    string? RequestedByUsername,
    bool IsMine,
    Guid? SourceSlotId,
    Guid? DestinationSlotId,
    Guid? ProductGroupId,
    Guid? PartId,
    Guid? ContainerTypeId,
    int Priority,
    DateTimeOffset CreatedAt,
    bool CanCancel,
    string? CancelDisabledCode,
    string? CancelDisabledMessage,
    PdaExecutionProjectionRecord Execution);

/// <summary>
/// Customer UI — Warehouse Preparation Console: the minimum, self-contained
/// projection of a WAITING_PREPARATION transport request needed by a
/// warehouse-authorized user to prepare it, independent of any single
/// machine's operational context. Never includes RDS/technical identifiers.
/// </summary>
public sealed record WarehousePreparationRequestRecord(
    Guid RequestId,
    long RequestNo,
    string Operation,
    Guid MachineId,
    string MachineCode,
    string MachineDisplayName,
    string? PartCode,
    string? ContainerTypeCode,
    Guid? SourceSlotId,
    string? SourceSlotCode,
    string? SourceSlotDisplayName,
    Guid? DestinationSlotId,
    string? DestinationSlotCode,
    string? DestinationSlotDisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? QueuedAt);

public sealed record OperationalContextDataRecord(
    MachineSummaryRecord Machine,
    Guid? ActiveVariantId,
    string? ActiveVariantCode,
    string? ActiveVariantName,
    Guid? ActiveLayoutId,
    string? ActiveLayoutCode,
    string? ActiveLayoutName,
    Guid? ActiveNavigationProfileId,
    string? ActiveNavigationProfileCode,
    long RuntimeRowVersion,
    DateTimeOffset? RuntimeUpdatedAt,
    IReadOnlyList<VariantLayoutOptionRecord> VariantLayouts,
    IReadOnlyList<SlotContextDataRecord> Slots,
    IReadOnlyList<PdaProductGroupRecord> ProductGroups,
    IReadOnlyList<ActiveTransportDataRecord> ActiveTransportRequests,
    IReadOnlyList<PdaTaskTemplateRecord> TaskTemplates);

public sealed record OperationalContextRecord(
    MachineSummaryRecord Machine,
    Guid? ActiveVariantId,
    string? ActiveVariantCode,
    string? ActiveVariantName,
    Guid? ActiveLayoutId,
    string? ActiveLayoutCode,
    string? ActiveLayoutName,
    Guid? ActiveNavigationProfileId,
    string? ActiveNavigationProfileCode,
    long RuntimeRowVersion,
    DateTimeOffset? RuntimeUpdatedAt,
    IReadOnlyList<VariantLayoutOptionRecord> VariantLayouts,
    IReadOnlyList<SlotContextRecord> Slots,
    IReadOnlyList<PdaProductGroupRecord> ProductGroups,
    IReadOnlyList<ActiveTransportRequestRecord> ActiveTransportRequests,
    IReadOnlyList<PdaTaskTemplateRecord> TaskTemplates);

public sealed record AuditEntry(
    Guid? ActorUserId,
    Guid? DeviceId,
    string Action,
    string EntityType,
    string? EntityId,
    string? BeforeJson,
    string? AfterJson,
    string? IpAddress,
    string? UserAgent,
    Guid CorrelationId);

public enum MachineAccessKind
{
    View,
    Operate,
    ChangeVariant,
    ManageSlots
}
