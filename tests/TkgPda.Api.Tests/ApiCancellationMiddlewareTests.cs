using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using TkgPda.Api;
using Xunit;

namespace TkgPda.Api.Tests;

/// <summary>
/// One coherent cancellation path in the API middleware. There were briefly two
/// catch clauses with the identical filter, so the second — which claimed to rethrow
/// — was unreachable and the claim was false. These tests pin the actual behaviour so
/// a duplicate cannot silently reappear.
/// </summary>
public sealed class ApiCancellationMiddlewareTests
{
    private static DefaultHttpContext Context(out MemoryStream body)
    {
        body = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        return context;
    }

    [Fact]
    public async Task A_client_disconnect_is_not_reported_as_a_server_error()
    {
        var context = Context(out var body);
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        context.RequestAborted = aborted.Token;

        var middleware = new ApiExceptionMiddleware(
            _ => throw new OperationCanceledException(aborted.Token),
            NullLogger<ApiExceptionMiddleware>.Instance);

        // Must not throw, must not turn into a 500, and must not write a body onto a
        // connection the client has already abandoned.
        await middleware.InvokeAsync(context);

        Assert.NotEqual(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(0, body.Length);
    }

    [Fact]
    public async Task An_internal_cancellation_is_still_a_server_error()
    {
        // RequestAborted is NOT cancelled: this is a genuine internal fault, not a
        // client disconnect, and it must not be quietly swallowed.
        var context = Context(out var body);

        var middleware = new ApiExceptionMiddleware(
            _ => throw new OperationCanceledException(),
            NullLogger<ApiExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains("INTERNAL_ERROR", System.Text.Encoding.UTF8.GetString(body.ToArray()), StringComparison.Ordinal);
    }
}
