using System.Text.Json;
using TkgPda.Domain;

namespace TkgPda.Application;

public enum RdsExecutionMode
{
    WindTask,
    RdsCoreNativeOrder
}

public abstract record RdsCoreOrderBlock(string BlockId, string Location)
{
    public abstract string Shape { get; }
}

public sealed record RdsCoreNavigationBlock(string BlockId, string Location)
    : RdsCoreOrderBlock(BlockId, Location)
{
    public override string Shape => "NAVIGATION";
}

public sealed record RdsCoreOperationBlock(
    string BlockId,
    string Location,
    string Operation,
    JsonElement? OperationArgs = null,
    JsonElement? PostAction = null)
    : RdsCoreOrderBlock(BlockId, Location)
{
    public override string Shape => "OPERATION";
}

public sealed record RdsCoreScriptBlock(
    string BlockId,
    string Location,
    string ScriptName,
    JsonElement? ScriptArgs = null,
    JsonElement? PostAction = null)
    : RdsCoreOrderBlock(BlockId, Location)
{
    public override string Shape => "SCRIPT";
    public string Operation => "Script";
}

public sealed record RdsCoreBinTaskBlock(
    string BlockId,
    string Location,
    string BinTask,
    string? GoodsId = null,
    JsonElement? PostAction = null)
    : RdsCoreOrderBlock(BlockId, Location)
{
    public override string Shape => "BIN_TASK";
}

public sealed class RdsCoreNormalOrder
{
    private RdsCoreNormalOrder(
        Guid transportRequestId,
        IReadOnlyList<RdsCoreOrderBlock> blocks,
        string? externalId,
        string? vehicle,
        string? group,
        string? label,
        IReadOnlyList<string>? keyRoute,
        string? keyTask,
        int? priority)
    {
        TransportRequestId = transportRequestId;
        Id = transportRequestId.ToString("D");
        ExternalId = Normalize(externalId);
        Vehicle = Normalize(vehicle);
        Group = Normalize(group);
        Label = Normalize(label);
        KeyRoute = keyRoute is null ? null : Array.AsReadOnly(keyRoute.ToArray());
        KeyTask = Normalize(keyTask);
        Priority = priority;
        Blocks = Array.AsReadOnly(blocks.ToArray());
    }

    public Guid TransportRequestId { get; }
    public string Id { get; }
    public string? ExternalId { get; }
    public string? Vehicle { get; }
    public string? Group { get; }
    public string? Label { get; }
    public IReadOnlyList<string>? KeyRoute { get; }
    public string? KeyTask { get; }
    public int? Priority { get; }
    public IReadOnlyList<RdsCoreOrderBlock> Blocks { get; }
    public bool Complete => true;

    public static RdsCoreNormalOrder Create(
        Guid transportRequestId,
        RdsCoreOrderBlock pickup,
        RdsCoreOrderBlock dropoff,
        string? externalId = null,
        string? vehicle = null,
        string? group = null,
        string? label = null,
        IReadOnlyList<string>? keyRoute = null,
        string? keyTask = null,
        int? priority = null)
    {
        if (transportRequestId == Guid.Empty)
            throw new ArgumentException("A TransportRequest UUID is required.", nameof(transportRequestId));
        ArgumentNullException.ThrowIfNull(pickup);
        ArgumentNullException.ThrowIfNull(dropoff);

        var expectedPickup = BlockId(transportRequestId, "pickup");
        var expectedDropoff = BlockId(transportRequestId, "dropoff");
        if (!string.Equals(pickup.BlockId, expectedPickup, StringComparison.Ordinal)
            || !string.Equals(dropoff.BlockId, expectedDropoff, StringComparison.Ordinal))
        {
            throw new ArgumentException("Native order block IDs must use the deterministic TransportRequest pickup/dropoff identities.");
        }

        ValidateBlock(pickup);
        ValidateBlock(dropoff);
        if (priority is < 0)
            throw new ArgumentOutOfRangeException(nameof(priority), "Priority cannot be negative.");
        var normalizedRoute = keyRoute?
            .Select(value => Normalize(value) ?? throw new ArgumentException("keyRoute cannot contain blank values.", nameof(keyRoute)))
            .ToArray();

        return new(
            transportRequestId,
            [pickup, dropoff],
            externalId,
            vehicle,
            group,
            label,
            normalizedRoute,
            keyTask,
            priority);
    }

    public static string PickupBlockId(Guid transportRequestId) => BlockId(transportRequestId, "pickup");
    public static string DropoffBlockId(Guid transportRequestId) => BlockId(transportRequestId, "dropoff");

    private static string BlockId(Guid requestId, string suffix)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("A TransportRequest UUID is required.", nameof(requestId));
        return $"{requestId:D}:{suffix}";
    }

    private static void ValidateBlock(RdsCoreOrderBlock block)
    {
        if (string.IsNullOrWhiteSpace(block.Location) || block.Location.Any(char.IsControl))
            throw new ArgumentException("Every native order block requires an explicit location.", nameof(block));

        switch (block)
        {
            case RdsCoreOperationBlock operation when string.IsNullOrWhiteSpace(operation.Operation):
                throw new ArgumentException("An operation block requires an explicit operation.", nameof(block));
            case RdsCoreScriptBlock script when string.IsNullOrWhiteSpace(script.ScriptName):
                throw new ArgumentException("A script block requires an explicit script name.", nameof(block));
            case RdsCoreBinTaskBlock binTask when string.IsNullOrWhiteSpace(binTask.BinTask):
                throw new ArgumentException("A binTask block requires an explicit binTask.", nameof(block));
        }

        switch (block)
        {
            case RdsCoreOperationBlock operation:
                ValidateOptionalObject(operation.OperationArgs, "operationArgs");
                ValidateOptionalObject(operation.PostAction, "postAction");
                break;
            case RdsCoreScriptBlock script:
                ValidateOptionalObject(script.ScriptArgs, "scriptArgs");
                ValidateOptionalObject(script.PostAction, "postAction");
                break;
            case RdsCoreBinTaskBlock binTask:
                ValidateOptionalObject(binTask.PostAction, "postAction");
                break;
        }
    }

    private static void ValidateOptionalObject(JsonElement? value, string name)
    {
        if (value is { ValueKind: not JsonValueKind.Object })
            throw new ArgumentException($"{name} must be a JSON object when supplied.", name);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record RdsCoreDiagnostic(JsonElement Value);

public sealed record RdsCoreOrderDiagnostics(
    IReadOnlyList<RdsCoreDiagnostic> Errors,
    IReadOnlyList<RdsCoreDiagnostic> Warnings,
    IReadOnlyList<RdsCoreDiagnostic> Notices)
{
    public static RdsCoreOrderDiagnostics Empty { get; } = new([], [], []);
}

public sealed record RdsCoreSetOrderResult(
    bool HttpAccepted,
    bool CoreAccepted,
    bool ExecutionValidated,
    int HttpStatusCode,
    int? Code,
    string? Message,
    bool DuplicateOrderId,
    RdsCoreOrderDiagnostics Diagnostics,
    JsonElement? RawResponse);

public enum RdsCoreKnownOrderState
{
    Created,
    Waiting,
    Running,
    Suspended,
    Finished,
    Failed,
    Stopped,
    Error,
    ToBeDispatched,
    Unknown
}

public enum RdsCoreKnownBlockState
{
    Created,
    Running,
    Waiting,
    Posting,
    Suspended,
    Finished,
    Failed,
    Stopped,
    ManualFinished,
    Error,
    Unknown
}

public sealed record RdsCoreOrderState(RdsCoreKnownOrderState Known, string? Raw);
public sealed record RdsCoreBlockState(RdsCoreKnownBlockState Known, string? Raw);

public sealed record RdsCoreBlockDetails(
    string? BlockId,
    string? Location,
    RdsCoreBlockState State,
    JsonElement Raw);

public sealed record RdsCoreOrderDetails(
    string? Id,
    string? ExternalId,
    RdsCoreOrderState State,
    IReadOnlyList<RdsCoreBlockDetails> Blocks,
    RdsCoreOrderDiagnostics Diagnostics,
    JsonElement Raw);

public sealed record RdsCoreOrderList(
    IReadOnlyList<RdsCoreOrderDetails> Orders,
    int? Total,
    int? Page,
    int? PageSize,
    JsonElement Raw);

public enum RdsCoreOrderSortField
{
    Id,
    State,
    CreateTime,
    TerminalTime,
    Vehicle,
    ExternalId,
    Priority
}

public enum RdsCoreOrderSortDirection
{
    Ascending,
    Descending
}

public sealed record RdsCoreOrderQuery(
    int Page = 1,
    int Size = 20,
    RdsCoreOrderSortField SortBy = RdsCoreOrderSortField.CreateTime,
    RdsCoreOrderSortDirection SortDirection = RdsCoreOrderSortDirection.Descending)
{
    public void Validate()
    {
        if (Page < 1) throw new ArgumentOutOfRangeException(nameof(Page), "Page must be at least 1.");
        if (Size is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(Size), "Size must be between 1 and 100.");
    }
}

public sealed record RdsCoreTerminateResult(
    bool HttpAccepted,
    bool CoreAccepted,
    int HttpStatusCode,
    int? Code,
    string? Message,
    JsonElement? RawResponse);

public sealed record RdsCoreSimpleResult(
    bool HttpAccepted,
    bool CoreAccepted,
    int HttpStatusCode,
    int? Code,
    string? Message,
    JsonElement? RawResponse);

public sealed record RdsCoreRobotTelemetry(
    string VehicleId,
    string? Model,
    string? CurrentMap,
    string? CurrentStation,
    string? LastStation,
    int ConnectionStatus,
    bool Dispatchable,
    bool IsLoaded,
    bool IsError,
    double BatteryLevel,
    double? X,
    double? Y,
    double? Angle,
    double? RemainingTime,
    string? CurrentOrderId,
    string? CurrentOrderState,
    IReadOnlyList<string> Alarms);

public interface IRdsCoreNativeOrderClient
{
    bool MutationEnabled { get; }
    RdsExecutionMode Mode { get; }
    Task<RdsCoreSetOrderResult> SetOrderAsync(RdsCoreNormalOrder order, Guid correlationId, CancellationToken cancellationToken = default);
    Task<RdsCoreOrderDetails> GetOrderDetailsAsync(string orderId, Guid correlationId, CancellationToken cancellationToken = default);
    Task<RdsCoreOrderList> GetOrdersAsync(RdsCoreOrderQuery query, Guid correlationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RdsCoreOrderDetails>> GetOrderDetailsByExternalIdAsync(string externalId, Guid correlationId, CancellationToken cancellationToken = default);
    Task<RdsCoreOrderDetails> GetOrderDetailsByBlockIdAsync(string blockId, Guid correlationId, CancellationToken cancellationToken = default);
    Task<RdsCoreBlockDetails> GetBlockDetailsByIdAsync(string blockId, Guid correlationId, CancellationToken cancellationToken = default);
    Task<RdsCoreTerminateResult> TerminateAsync(string orderId, Guid correlationId, CancellationToken cancellationToken = default);
    Task<RdsCoreSimpleResult> SetPriorityAsync(string orderId, int priority, Guid correlationId, CancellationToken cancellationToken = default);
    Task<RdsCoreSimpleResult> ClearRobotAllErrorAsync(IReadOnlyList<string> vehicles, Guid correlationId, CancellationToken cancellationToken = default);
    Task<RdsCoreSimpleResult> GotoSitePauseAsync(IReadOnlyList<string> vehicles, Guid correlationId, CancellationToken cancellationToken = default);
    Task<RdsCoreSimpleResult> GotoSiteResumeAsync(IReadOnlyList<string> vehicles, Guid correlationId, CancellationToken cancellationToken = default);
    Task<RdsCoreSimpleResult> ManualFinishedAsync(IReadOnlyList<string> vehicles, Guid correlationId, CancellationToken cancellationToken = default);
    Task<RdsCoreSimpleResult> RedoFailedOrderAsync(IReadOnlyList<string> vehicles, Guid correlationId, CancellationToken cancellationToken = default);
    Task<RdsCoreSimpleResult> SetDispatchableAsync(IReadOnlyList<string> vehicles, string type, Guid correlationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RdsCoreRobotTelemetry>> GetRobotsStatusAsync(Guid correlationId, CancellationToken cancellationToken = default);
}

public sealed class RdsCoreNativeOrderException(string code, string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public string Code { get; } = code;
}

public enum RdsCoreReconciliationMeaning
{
    Pending,
    Running,
    SuspendedRecoverable,
    CompletedCandidate,
    FailedCandidate,
    CancelledCandidate,
    Unknown
}

public sealed record RdsCoreReconciliationDecision(
    RdsCoreReconciliationMeaning Meaning,
    IReadOnlyList<TransportRequestStatus> LegalTransitionPath,
    bool AutomaticMutationAllowed,
    string? UnresolvedReason);

public static class RdsCoreOrderReconciliationPolicy
{
    public static RdsCoreReconciliationDecision Decide(
        TransportRequestStatus current,
        RdsCoreOrderState rdsState)
    {
        ArgumentNullException.ThrowIfNull(rdsState);
        var meaning = rdsState.Known switch
        {
            RdsCoreKnownOrderState.Created or RdsCoreKnownOrderState.Waiting or RdsCoreKnownOrderState.ToBeDispatched => RdsCoreReconciliationMeaning.Pending,
            RdsCoreKnownOrderState.Running => RdsCoreReconciliationMeaning.Running,
            RdsCoreKnownOrderState.Suspended => RdsCoreReconciliationMeaning.SuspendedRecoverable,
            RdsCoreKnownOrderState.Finished => RdsCoreReconciliationMeaning.CompletedCandidate,
            RdsCoreKnownOrderState.Failed or RdsCoreKnownOrderState.Error => RdsCoreReconciliationMeaning.FailedCandidate,
            RdsCoreKnownOrderState.Stopped => RdsCoreReconciliationMeaning.CancelledCandidate,
            _ => RdsCoreReconciliationMeaning.Unknown
        };

        if (meaning == RdsCoreReconciliationMeaning.SuspendedRecoverable)
            return new(meaning, [], false, "TKG has no SUSPENDED execution state; operator/integration reconciliation is required.");
        if (meaning == RdsCoreReconciliationMeaning.Unknown)
            return new(meaning, [], false, "Unknown RDSCore states are evidence only and cannot mutate TKG state.");
        if (current is TransportRequestStatus.Completed or TransportRequestStatus.Failed or TransportRequestStatus.Cancelled)
        {
            var alreadyAligned = current == TransportRequestStatus.Completed && meaning == RdsCoreReconciliationMeaning.CompletedCandidate
                || current == TransportRequestStatus.Failed && meaning == RdsCoreReconciliationMeaning.FailedCandidate
                || current == TransportRequestStatus.Cancelled && meaning == RdsCoreReconciliationMeaning.CancelledCandidate;
            return new(meaning, [], false, alreadyAligned ? null : "A terminal TKG state cannot be rewritten by later RDS evidence.");
        }

        var target = meaning switch
        {
            RdsCoreReconciliationMeaning.Pending => TransportRequestStatus.SentToRds,
            RdsCoreReconciliationMeaning.Running => TransportRequestStatus.Running,
            RdsCoreReconciliationMeaning.CompletedCandidate => TransportRequestStatus.Completed,
            RdsCoreReconciliationMeaning.FailedCandidate => TransportRequestStatus.Failed,
            RdsCoreReconciliationMeaning.CancelledCandidate => TransportRequestStatus.Cancelled,
            _ => current
        };
        var path = BuildLegalPath(current, target);
        return path is null
            ? new(meaning, [], false, $"No legal TKG transition path exists from {current.ToDatabase()} to {target.ToDatabase()}.")
            : new(meaning, path, path.Count > 0, null);
    }

    private static IReadOnlyList<TransportRequestStatus>? BuildLegalPath(
        TransportRequestStatus current,
        TransportRequestStatus target)
    {
        if (current == target) return [];
        var queue = new Queue<(TransportRequestStatus Status, IReadOnlyList<TransportRequestStatus> Path)>();
        var visited = new HashSet<TransportRequestStatus> { current };
        queue.Enqueue((current, []));
        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            foreach (var next in TransportStateMachine.NextStates(item.Status))
            {
                if (!visited.Add(next)) continue;
                var path = item.Path.Append(next).ToArray();
                if (next == target) return path;
                queue.Enqueue((next, path));
            }
        }
        return null;
    }
}
