using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Npgsql;
using TkgPda.Application;
using TkgPda.Domain;

namespace TkgPda.Api;

public sealed class ApiException(int statusCode, string code, string message) : InvalidOperationException(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException error)
        {
            await ApiErrorWriter.WriteAsync(context, error.StatusCode, error.Code, error.Message);
        }
        catch (WorkflowRuleException error)
        {
            var status = WorkflowHttpStatus(error.Code);
            await ApiErrorWriter.WriteAsync(context, status, error.Code, error.Message);
        }
        catch (AdminRuleException error)
        {
            var status = AdminHttpStatus(error.Code);
            await ApiErrorWriter.WriteAsync(context, status, error.Code, error.Message);
        }
        catch (RdsIntegrationRuleException error)
        {
            var status = error.Code.EndsWith("_NOT_FOUND", StringComparison.Ordinal)
                ? StatusCodes.Status404NotFound
                : error.Code is "NAVIGATION_PROFILE_REQUIRED" or "ID_REQUIRED"
                    ? StatusCodes.Status400BadRequest
                    : error.Code is "MACHINE_REGION_MAPPING_FORBIDDEN" or "RDS_WORKSITE_NOT_PHYSICAL"
                        ? StatusCodes.Status422UnprocessableEntity
                        : StatusCodes.Status409Conflict;
            await ApiErrorWriter.WriteAsync(context, status, error.Code, error.Message);
        }
        catch (RdsReadException error)
        {
            logger.LogWarning("RDS read failed with {Code} at {Endpoint} for correlation {CorrelationId}.", error.Code, error.Endpoint, error.CorrelationId);
            await ApiErrorWriter.WriteAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                error.Code,
                "The read-only RDS service is temporarily unavailable.");
        }
        catch (BadHttpRequestException error)
        {
            await ApiErrorWriter.WriteAsync(context, StatusCodes.Status400BadRequest, "INVALID_HTTP_REQUEST", error.Message);
        }
        catch (JsonException)
        {
            await ApiErrorWriter.WriteAsync(context, StatusCodes.Status400BadRequest, "INVALID_JSON", "The request body is not valid JSON.");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request {TraceIdentifier} was cancelled by the client.", context.TraceIdentifier);
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.CheckViolation)
        {
            logger.LogInformation("Configuration constraint rejected request {TraceIdentifier}: {ConstraintName}.", context.TraceIdentifier, error.ConstraintName);
            await ApiErrorWriter.WriteAsync(
                context,
                StatusCodes.Status409Conflict,
                "CONFIGURATION_CONFLICT",
                "The requested configuration conflicts with an existing record or protected reference.");
        }
        catch (NpgsqlException error)
        {
            logger.LogWarning(error, "PostgreSQL was unavailable for request {TraceIdentifier}.", context.TraceIdentifier);
            await ApiErrorWriter.WriteAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "DATABASE_UNAVAILABLE",
                "The database is temporarily unavailable.");
        }
        catch (Exception error)
        {
            logger.LogError(error, "Unhandled API error for request {TraceIdentifier}.", context.TraceIdentifier);
            await ApiErrorWriter.WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "INTERNAL_ERROR",
                "An unexpected server error occurred.");
        }
    }

    private static int WorkflowHttpStatus(string code) => code switch
    {
        "REQUEST_NOT_FOUND" or "SLOT_NOT_FOUND" or "PART_NOT_FOUND" or "CONTAINER_NOT_FOUND" or "TASK_TYPE_NOT_FOUND"
            => StatusCodes.Status404NotFound,
        "IDEMPOTENCY_KEY_REQUIRED" or "IDEMPOTENCY_KEY_TOO_LONG" or "SOURCE_REQUIRED" or "DESTINATION_REQUIRED"
            or "SAME_SOURCE_DESTINATION" or "INVALID_PRIORITY" or "REQUEST_ID_REQUIRED"
            => StatusCodes.Status400BadRequest,
        "IDEMPOTENCY_KEY_REUSED" or "SLOT_ALREADY_RESERVED" or "ACTIVE_TRANSPORT_EXISTS"
            or "RUNTIME_CONTEXT_MISMATCH" or "NAVIGATION_CONTEXT_MISMATCH" or "VARIANT_LAYOUT_CONFLICT"
            or "NAVIGATION_PROFILE_CONFLICT" or "PRODUCT_GROUP_CONFLICT" or "PART_CONFLICT"
            or "CONTAINER_CONFLICT" or "INVALID_SLOT_STATE" or "ILLEGAL_STATUS_TRANSITION"
            or "CANCELLATION_AFTER_EXECUTION_DEFERRED" or "REQUEST_LOCK_SCOPE_CHANGED"
            => StatusCodes.Status409Conflict,
        "MACHINE_REGION_FORBIDDEN" or "CROSS_MACHINE_SLOT" or "CROSS_LAYOUT_SLOT" or "SLOT_DISABLED"
            => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status422UnprocessableEntity
    };

    private static int AdminHttpStatus(string code)
    {
        if (code.EndsWith("_NOT_FOUND", StringComparison.Ordinal))
        {
            return StatusCodes.Status404NotFound;
        }

        return code switch
        {
            "WEAK_PASSWORD" or "INVALID_FIELD" or "INVALID_ENUM_VALUE" or "INVALID_JSON_OBJECT"
                or "ID_REQUIRED" or "ROLE_REQUIRED" or "UNKNOWN_ROLE" or "CONCURRENCY_TOKEN_REQUIRED"
                => StatusCodes.Status400BadRequest,
            "INVALID_SLOT_GEOMETRY" or "INVALID_CONTAINER_DIMENSIONS"
                => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status409Conflict
        };
    }
}

public static class ApiErrorWriter
{
    public static async Task WriteAsync(HttpContext context, int statusCode, string code, string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.CacheControl = "no-store";
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = ReasonPhrases.GetReasonPhrase(statusCode),
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = context.Items.TryGetValue(ApiContextKeys.CorrelationId, out var value)
            ? value
            : context.TraceIdentifier;
        await context.Response.WriteAsJsonAsync(problem, cancellationToken: context.RequestAborted);
    }
}
