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

public sealed record VariantLayoutOptionRecord(
    Guid VariantId,
    string VariantCode,
    string VariantName,
    Guid LayoutId,
    string LayoutCode,
    string LayoutName,
    bool IsDefault);

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
    DateTimeOffset? RuntimeUpdatedAt);

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
    IReadOnlyList<SlotContextRecord> Slots);

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
