namespace TkgPda.Api;

public sealed record LoginRequest(string Username, string Password);

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

public sealed record VariantSwitchHttpRequest(
    Guid VariantId,
    Guid LayoutId,
    Guid? NavigationProfileId);

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
    bool IdempotentReplay);
