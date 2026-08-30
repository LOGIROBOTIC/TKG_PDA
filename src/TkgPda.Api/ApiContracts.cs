using TkgPda.Application;

namespace TkgPda.Api;

public sealed record LoginRequest(string? Username, string Password, string? EmployeeNumber = null);

public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt,
    AuthenticatedUserResponse User,
    DeviceResponse Device);

public sealed record AuthenticatedUserResponse(
    Guid Id,
    string Username,
    string FullName,
    string? EmployeeNo,
    IReadOnlyList<string> Roles);

public sealed record DeviceResponse(Guid Id, string Code, string Name, string Type);

public sealed record CreateTransportHttpRequest(
    Guid MachineId,
    Guid VariantId,
    Guid LayoutId,
    Guid? NavigationProfileId,
    Guid? ProductGroupId,
    Guid? PartId,
    Guid? ContainerTypeId,
    string Operation,
    Guid? SourceSlotId,
    Guid? DestinationSlotId,
    int Priority = 100);

public sealed record CreateTransportBatchItemHttpRequest(
    string IdempotencyKey,
    Guid MachineId,
    Guid VariantId,
    Guid LayoutId,
    Guid? NavigationProfileId,
    Guid? ProductGroupId,
    Guid? PartId,
    Guid? ContainerTypeId,
    string Operation,
    Guid? SourceSlotId,
    Guid? DestinationSlotId,
    int Priority = 100);

public sealed record CreateTransportBatchHttpRequest(
    string IdempotencyKey,
    Guid MachineId,
    IReadOnlyList<CreateTransportBatchItemHttpRequest> Items);

public sealed record TransportBatchHttpResponse(
    Guid BatchId,
    Guid MachineId,
    bool IdempotentReplay,
    IReadOnlyList<TransportHttpResponse> Items);

public sealed record VariantSwitchHttpRequest(
    Guid VariantId,
    Guid LayoutId,
    Guid? NavigationProfileId);

public sealed record UpdateLoadStateHttpRequest(string LoadState);

public sealed record UpdateLoadStateHttpResponse(
    Guid MachineId,
    Guid SlotId,
    string OccupancyState,
    string LoadState,
    long RowVersion,
    bool IdempotentReplay,
    RdsLoadContentSyncResult RdsContentSync);

public sealed record TransportHttpResponse(
    Guid Id,
    long RequestNo,
    string Status,
    string Operation,
    Guid MachineId,
    Guid VariantId,
    Guid LayoutId,
    Guid? NavigationProfileId,
    Guid? ProductGroupId,
    Guid? PartId,
    Guid? ContainerTypeId,
    Guid? SourceSlotId,
    Guid? DestinationSlotId,
    int Priority,
    bool IdempotentReplay,
    bool RdsCancellationPending = false);
