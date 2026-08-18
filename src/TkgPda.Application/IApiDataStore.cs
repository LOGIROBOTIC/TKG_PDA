using TkgPda.Domain;

namespace TkgPda.Application;

public interface IApiDataStore
{
    Task<AuthUserRecord?> FindUserByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<DeviceRecord?> FindDeviceAsync(string deviceCode, CancellationToken cancellationToken = default);

    Task MarkLoginSucceededAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<MachinePermissionRecord?> FindMachinePermissionAsync(
        Guid userId,
        Guid machineId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MachineSummaryRecord>> ListVisibleMachinesAsync(
        Guid userId,
        bool administrator,
        CancellationToken cancellationToken = default);

    Task<OperationalContextRecord?> GetOperationalContextAsync(
        Guid userId,
        Guid machineId,
        bool administrator,
        CancellationToken cancellationToken = default);

    Task<TransportRequestSnapshot?> FindTransportRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task WriteAuditAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);
}
