namespace TkgPda.Domain;

public enum TransportRequestStatus
{
    Created,
    Validating,
    Queued,
    SentToRds,
    Running,
    Completed,
    Failed,
    Cancelled
}

public enum TransportTaskType
{
    BringEmpty,
    RemoveFull,
    RemoveEmpty
}

public enum RequestOrigin
{
    LineOperator,
    WarehouseOperator,
    Admin,
    Plc,
    Api
}

public enum SlotRole
{
    Machine,
    Staging,
    EmptyStorage,
    FullStorage,
    Warehouse,
    Pickup,
    Drop,
    Shared,
    Other
}

public enum OccupancyState
{
    Vacant,
    Occupied,
    Unknown
}

public enum LoadState
{
    Empty,
    Full,
    Unknown
}

public static class WorkflowCode
{
    public static string ToDatabase(this TransportRequestStatus value) => value switch
    {
        TransportRequestStatus.Created => "CREATED",
        TransportRequestStatus.Validating => "VALIDATING",
        TransportRequestStatus.Queued => "QUEUED",
        TransportRequestStatus.SentToRds => "SENT_TO_RDS",
        TransportRequestStatus.Running => "RUNNING",
        TransportRequestStatus.Completed => "COMPLETED",
        TransportRequestStatus.Failed => "FAILED",
        TransportRequestStatus.Cancelled => "CANCELLED",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static TransportRequestStatus ParseTransportStatus(string value) => value switch
    {
        "CREATED" => TransportRequestStatus.Created,
        "VALIDATING" => TransportRequestStatus.Validating,
        "QUEUED" => TransportRequestStatus.Queued,
        "SENT_TO_RDS" => TransportRequestStatus.SentToRds,
        "RUNNING" => TransportRequestStatus.Running,
        "COMPLETED" => TransportRequestStatus.Completed,
        "FAILED" => TransportRequestStatus.Failed,
        "CANCELLED" => TransportRequestStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown SQL transport status.")
    };

    public static string ToDatabase(this TransportTaskType value) => value switch
    {
        TransportTaskType.BringEmpty => "BRING_EMPTY",
        TransportTaskType.RemoveFull => "REMOVE_FULL",
        TransportTaskType.RemoveEmpty => "REMOVE_EMPTY",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static TransportTaskType ParseTaskType(string value) => value switch
    {
        "BRING_EMPTY" => TransportTaskType.BringEmpty,
        "REMOVE_FULL" => TransportTaskType.RemoveFull,
        "REMOVE_EMPTY" => TransportTaskType.RemoveEmpty,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown SQL task type.")
    };

    public static string ToDatabase(this RequestOrigin value) => value switch
    {
        RequestOrigin.LineOperator => "LINE_OPERATOR",
        RequestOrigin.WarehouseOperator => "WAREHOUSE_OPERATOR",
        RequestOrigin.Admin => "ADMIN",
        RequestOrigin.Plc => "PLC",
        RequestOrigin.Api => "API",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static RequestOrigin ParseRequestOrigin(string value) => value switch
    {
        "LINE_OPERATOR" => RequestOrigin.LineOperator,
        "WAREHOUSE_OPERATOR" => RequestOrigin.WarehouseOperator,
        "ADMIN" => RequestOrigin.Admin,
        "PLC" => RequestOrigin.Plc,
        "API" => RequestOrigin.Api,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown SQL request origin.")
    };

    public static SlotRole ParseSlotRole(string value) => value switch
    {
        "MACHINE" => SlotRole.Machine,
        "STAGING" => SlotRole.Staging,
        "EMPTY_STORAGE" => SlotRole.EmptyStorage,
        "FULL_STORAGE" => SlotRole.FullStorage,
        "WAREHOUSE" => SlotRole.Warehouse,
        "PICKUP" => SlotRole.Pickup,
        "DROP" => SlotRole.Drop,
        "SHARED" => SlotRole.Shared,
        "OTHER" => SlotRole.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown SQL slot role.")
    };

    public static OccupancyState ParseOccupancy(string value) => value switch
    {
        "VACANT" => OccupancyState.Vacant,
        "OCCUPIED" => OccupancyState.Occupied,
        "UNKNOWN" => OccupancyState.Unknown,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown SQL occupancy state.")
    };

    public static string ToDatabase(this OccupancyState value) => value.ToString().ToUpperInvariant();

    public static LoadState ParseLoad(string value) => value switch
    {
        "EMPTY" => LoadState.Empty,
        "FULL" => LoadState.Full,
        "UNKNOWN" => LoadState.Unknown,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown SQL load state.")
    };

    public static string ToDatabase(this LoadState value) => value.ToString().ToUpperInvariant();
}
