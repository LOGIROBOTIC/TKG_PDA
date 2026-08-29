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
