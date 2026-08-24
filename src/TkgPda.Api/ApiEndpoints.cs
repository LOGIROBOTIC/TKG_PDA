using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TkgPda.Application;
using TkgPda.Domain;

namespace TkgPda.Api;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapTkgPdaApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/auth/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting("login");

        endpoints.MapGet("/api/v1/auth/me", CurrentUserAsync)
            .RequireAuthorization();

        endpoints.MapGet("/api/v1/machines", ListMachinesAsync)
            .RequireAuthorization();

        endpoints.MapGet("/api/v1/machines/{machineId:guid}/operational-context", OperationalContextAsync)
            .RequireAuthorization();

        endpoints.MapPost("/api/v1/machines/{machineId:guid}/variant-selection", SwitchVariantAsync)
            .RequireAuthorization(PolicyNames.VariantManager);

        endpoints.MapPost("/api/v1/transport-requests", CreateTransportAsync)
            .RequireAuthorization(PolicyNames.Operator);

        endpoints.MapGet("/api/v1/transport-requests/{requestId:guid}", GetTransportAsync)
            .RequireAuthorization();

        endpoints.MapPost("/api/v1/transport-requests/{requestId:guid}/cancel", CancelTransportAsync)
            .RequireAuthorization(PolicyNames.Operator);

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext context,
        IApiDataStore dataStore,
        Pbkdf2PasswordHasher passwordHasher,
        JwtTokenService tokenService,
        CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        if (username.Length is < 1 or > 100 || string.IsNullOrEmpty(request.Password) || request.Password.Length > 256)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "INVALID_LOGIN_REQUEST", "Username and password are required and must fit the supported limits.");
        }

        var deviceCode = context.Request.Headers[ApiHeaders.DeviceCode].ToString().Trim();
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "DEVICE_CONTEXT_REQUIRED", "Login requires an X-Device-Code header.");
        }

        var device = await dataStore.FindDeviceAsync(deviceCode, cancellationToken);
        if (device is null || !device.IsActive || device.Type != "PDA")
        {
            throw new ApiException(StatusCodes.Status403Forbidden, "DEVICE_NOT_AUTHORIZED", "The client device is unknown, inactive or not a PDA.");
        }

        var user = await dataStore.FindUserByUsernameAsync(username, cancellationToken);
        var passwordValid = user is null
            ? passwordHasher.VerifyUnknownUser(request.Password)
            : passwordHasher.Verify(request.Password, user.PasswordHash);
        if (user is null || !user.IsActive || !passwordValid || user.Roles.Count == 0)
        {
            await WriteAuditAsync(
                dataStore,
                context,
                null,
                device.Id,
                "AUTH_LOGIN_FAILED",
                "AUTH_USER",
                username,
                new { reason = "INVALID_CREDENTIALS" },
                cancellationToken);
            throw new ApiException(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", "The username or password is invalid.");
        }

        await dataStore.MarkLoginSucceededAsync(user.Id, cancellationToken);
        var token = tokenService.Issue(user, device);
        await WriteAuditAsync(
            dataStore,
            context,
            user.Id,
            device.Id,
            "AUTH_LOGIN_SUCCEEDED",
            "AUTH_USER",
            user.Id.ToString(),
            new { roles = user.Roles, device = device.Code },
            cancellationToken);
        context.Response.Headers.CacheControl = "no-store";

        return Results.Ok(new LoginResponse(
            token.Token,
            "Bearer",
            token.ExpiresAt,
            new AuthenticatedUserResponse(user.Id, user.Username, user.FullName, user.EmployeeNo, user.Roles),
            new DeviceResponse(device.Id, device.Code, device.Name, device.Type)));
    }

    private static IResult CurrentUserAsync(HttpContext context)
    {
        var principal = context.User;
        var device = context.Items.TryGetValue(ApiContextKeys.Device, out var value) && value is DeviceRecord record
            ? new DeviceResponse(record.Id, record.Code, record.Name, record.Type)
            : null;
        return Results.Ok(new
        {
            id = principal.UserId(),
            username = principal.Identity?.Name,
            fullName = principal.FindFirstValue(ApiClaimNames.FullName),
            roles = principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Order().ToArray(),
            clientType = principal.FindFirstValue(ApiClaimNames.ClientType),
            device
        });
    }

    private static async Task<IResult> ListMachinesAsync(
        HttpContext context,
        IApiDataStore dataStore,
        CancellationToken cancellationToken)
    {
        var machines = await dataStore.ListVisibleMachinesAsync(
            context.User.UserId(),
            context.User.IsAdministrator(),
            cancellationToken);
        return Results.Ok(machines);
    }

    private static async Task<IResult> OperationalContextAsync(
        Guid machineId,
        HttpContext context,
        MachineAccessService access,
        PdaOperationalContextService operationalContext,
        CancellationToken cancellationToken)
    {
        _ = await access.EnsureAsync(context.User, machineId, MachineAccessKind.View, cancellationToken);
        var roles = context.User.FindAll(ClaimTypes.Role)
            .Select(static claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);
        var result = await operationalContext.GetAsync(
            new PdaActorContext(context.User.UserId(), roles, context.User.IsDeviceBound()),
            machineId,
            context.User.IsAdministrator(),
            cancellationToken);
        return result is null
            ? throw new ApiException(StatusCodes.Status404NotFound, "MACHINE_NOT_FOUND", "The requested machine does not exist or is inactive.")
            : Results.Ok(result);
    }

    private static async Task<IResult> CreateTransportAsync(
        CreateTransportHttpRequest request,
        HttpContext context,
        MachineAccessService access,
        TransportWorkflowService workflow,
        IApiDataStore dataStore,
        CancellationToken cancellationToken)
    {
        _ = Device(context);
        _ = await access.EnsureAsync(context.User, request.MachineId, MachineAccessKind.Operate, cancellationToken);
        var idempotencyKey = context.Request.Headers[ApiHeaders.IdempotencyKey].ToString().Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "IDEMPOTENCY_KEY_REQUIRED", "Transport creation requires an Idempotency-Key header.");
        }

        var operation = ParseOperation(request.Operation);
        EnsureTransportOperationAllowed(context.User, operation);
        var command = new CreateTransportRequest(
            idempotencyKey,
            RequestOrigin.Api,
            context.User.UserId(),
            request.MachineId,
            request.VariantId,
            request.LayoutId,
            request.NavigationProfileId,
            request.ProductGroupId,
            request.PartId,
            request.ContainerTypeId,
            operation,
            request.SourceSlotId,
            request.DestinationSlotId,
            request.Priority);
        var result = await workflow.CreateAsync(command, cancellationToken);
        context.Response.Headers[ApiHeaders.IdempotencyReplayed] = result.WasIdempotentReplay ? "true" : "false";
        context.Response.Headers.Location = $"/api/v1/transport-requests/{result.Request.Id}";
        await WriteAuditAsync(
            dataStore,
            context,
            context.User.UserId(),
            Device(context).Id,
            result.WasIdempotentReplay ? "TRANSPORT_REQUEST_REPLAYED" : "TRANSPORT_REQUEST_CREATED",
            "TRANSPORT_REQUEST",
            result.Request.Id.ToString(),
            new { result.Request.RequestNo, status = result.Request.Status.ToDatabase() },
            cancellationToken);

        return Results.Json(
            ToResponse(result.Request, result.WasIdempotentReplay),
            statusCode: result.WasIdempotentReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetTransportAsync(
        Guid requestId,
        HttpContext context,
        IApiDataStore dataStore,
        MachineAccessService access,
        CancellationToken cancellationToken)
    {
        var request = await dataStore.FindTransportRequestAsync(requestId, cancellationToken)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "REQUEST_NOT_FOUND", "The transport request does not exist.");
        _ = await access.EnsureAsync(context.User, request.MachineId, MachineAccessKind.View, cancellationToken);
        return Results.Ok(ToResponse(request, false));
    }

    private static async Task<IResult> CancelTransportAsync(
        Guid requestId,
        HttpContext context,
        IApiDataStore dataStore,
        MachineAccessService access,
        TransportWorkflowService workflow,
        CancellationToken cancellationToken)
    {
        var existing = await dataStore.FindTransportRequestAsync(requestId, cancellationToken)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "REQUEST_NOT_FOUND", "The transport request does not exist.");
        _ = await access.EnsureAsync(context.User, existing.MachineId, MachineAccessKind.Operate, cancellationToken);
        EnsureCancellationAllowed(context.User, existing);
        var result = await workflow.CancelAsync(requestId, context.User.UserId(), cancellationToken);
        await WriteAuditAsync(
            dataStore,
            context,
            context.User.UserId(),
            Device(context).Id,
            result.WasAlreadyCancelled ? "TRANSPORT_CANCELLATION_REPLAYED" : "TRANSPORT_REQUEST_CANCELLED",
            "TRANSPORT_REQUEST",
            requestId.ToString(),
            new { status = result.Request.Status.ToDatabase() },
            cancellationToken);
        return Results.Ok(ToResponse(result.Request, result.WasAlreadyCancelled));
    }

    private static async Task<IResult> SwitchVariantAsync(
        Guid machineId,
        VariantSwitchHttpRequest request,
        HttpContext context,
        MachineAccessService access,
        TransportWorkflowService workflow,
        IApiDataStore dataStore,
        CancellationToken cancellationToken)
    {
        _ = await access.EnsureAsync(context.User, machineId, MachineAccessKind.ChangeVariant, cancellationToken);
        await workflow.SwitchVariantAsync(
            new VariantSelection(machineId, request.VariantId, request.LayoutId, request.NavigationProfileId, context.User.UserId()),
            cancellationToken);
        await WriteAuditAsync(
            dataStore,
            context,
            context.User.UserId(),
            Device(context).Id,
            "MACHINE_VARIANT_SWITCHED",
            "MACHINE_RUNTIME_STATE",
            machineId.ToString(),
            request,
            cancellationToken);
        return Results.Ok(new
        {
            machineId,
            activeVariantId = request.VariantId,
            activeLayoutId = request.LayoutId,
            activeNavigationProfileId = request.NavigationProfileId
        });
    }

    private static TransportTaskType ParseOperation(string operation) => operation?.Trim().ToUpperInvariant() switch
    {
        "BRING_EMPTY" => TransportTaskType.BringEmpty,
        "REMOVE_FULL" => TransportTaskType.RemoveFull,
        "REMOVE_EMPTY" => TransportTaskType.RemoveEmpty,
        _ => throw new ApiException(StatusCodes.Status400BadRequest, "INVALID_OPERATION", "Operation must be BRING_EMPTY, REMOVE_FULL or REMOVE_EMPTY.")
    };

    private static void EnsureTransportOperationAllowed(ClaimsPrincipal principal, TransportTaskType operation)
    {
        var warehouseOnly = principal.IsInRole(RoleCodes.WarehouseOperator)
            && !principal.IsInRole(RoleCodes.Admin)
            && !principal.IsInRole(RoleCodes.Supervisor)
            && !principal.IsInRole(RoleCodes.LineOperator);
        if (warehouseOnly && operation != TransportTaskType.BringEmpty)
        {
            throw new ApiException(
                StatusCodes.Status403Forbidden,
                "OPERATION_NOT_ALLOWED_FOR_ROLE",
                "WAREHOUSE_OPERATOR may create BRING_EMPTY requests but may not initiate line-side removal operations.");
        }
    }

    private static void EnsureCancellationAllowed(ClaimsPrincipal principal, TransportRequestSnapshot request)
    {
        var warehouseOnly = principal.IsInRole(RoleCodes.WarehouseOperator)
            && !principal.IsInRole(RoleCodes.Admin)
            && !principal.IsInRole(RoleCodes.Supervisor)
            && !principal.IsInRole(RoleCodes.LineOperator);
        if (warehouseOnly && (request.TaskType != TransportTaskType.BringEmpty || request.RequestedBy != principal.UserId()))
        {
            throw new ApiException(
                StatusCodes.Status403Forbidden,
                "CANCELLATION_NOT_ALLOWED_FOR_ROLE",
                "WAREHOUSE_OPERATOR may cancel only BRING_EMPTY requests created by the same user.");
        }
    }

    private static TransportHttpResponse ToResponse(TransportRequestSnapshot request, bool replay) => new(
        request.Id,
        request.RequestNo,
        request.Status.ToDatabase(),
        request.TaskType.ToDatabase(),
        request.MachineId,
        request.VariantId,
        request.LayoutId,
        request.NavigationProfileId,
        request.ProductGroupId,
        request.PartId,
        request.ContainerTypeId,
        request.SourceSlotId,
        request.DestinationSlotId,
        request.Priority,
        replay);

    private static DeviceRecord Device(HttpContext context) =>
        context.Items.TryGetValue(ApiContextKeys.Device, out var value) && value is DeviceRecord device
            ? device
            : throw new ApiException(StatusCodes.Status401Unauthorized, "DEVICE_CONTEXT_REQUIRED", "A validated device context is required.");

    private static async Task WriteAuditAsync(
        IApiDataStore dataStore,
        HttpContext context,
        Guid? actorUserId,
        Guid? deviceId,
        string action,
        string entityType,
        string? entityId,
        object? after,
        CancellationToken cancellationToken)
    {
        var correlationId = context.Items.TryGetValue(ApiContextKeys.CorrelationId, out var value) && value is Guid id
            ? id
            : Guid.NewGuid();
        var userAgent = context.Request.Headers.UserAgent.ToString();
        await dataStore.WriteAuditAsync(
            new AuditEntry(
                actorUserId,
                deviceId,
                action,
                entityType,
                entityId,
                null,
                after is null ? null : JsonSerializer.Serialize(after),
                context.Connection.RemoteIpAddress?.ToString(),
                userAgent.Length > 2000 ? userAgent[..2000] : userAgent,
                correlationId),
            cancellationToken);
    }
}
