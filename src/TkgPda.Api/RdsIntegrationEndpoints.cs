using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TkgPda.Application;

namespace TkgPda.Api;

public sealed record RdsCallbackHttpRequest(string EventType, JsonElement RawPayload, DateTimeOffset? EventTimestamp);

public static class RdsIntegrationEndpoints
{
    public static IEndpointRouteBuilder MapTkgPdaRdsIntegrationApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/integrations/rds/events", CallbackAsync)
            .AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> CallbackAsync(
        RdsCallbackHttpRequest request,
        HttpContext context,
        RdsOptions options,
        RdsExecutionCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.CallbackKey))
        {
            throw new ApiException(StatusCodes.Status503ServiceUnavailable, "RDS_CALLBACK_DISABLED", "RDS callback processing is not configured.");
        }

        var supplied = context.Request.Headers["X-TKG-RDS-Key"].ToString();
        if (!FixedTimeEquals(supplied, options.CallbackKey))
        {
            throw new ApiException(StatusCodes.Status401Unauthorized, "RDS_CALLBACK_UNAUTHORIZED", "The RDS callback key is invalid.");
        }

        if (string.IsNullOrWhiteSpace(request.EventType) || request.EventType.Length > 100)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "RDS_EVENT_INVALID", "A supported callback event type is required.");
        }

        var result = await coordinator.ProcessCallbackAsync(
            new(request.EventType, request.RawPayload, request.EventTimestamp),
            cancellationToken);
        return Results.Accepted(value: result);
    }

    private static bool FixedTimeEquals(string supplied, string expected)
    {
        var left = Encoding.UTF8.GetBytes(supplied);
        var right = Encoding.UTF8.GetBytes(expected);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
