using TkgPda.Api;
using TkgPda.Application;
using Xunit;

namespace TkgPda.Api.Tests;

/// <summary>
/// Once the robot has answered, the physical outcome is authoritative. These pin the
/// guarantee that the audit write cannot alter it — including when the audit write is
/// cancelled, which is the case that used to escape the filtered catches and either
/// mask a real physical failure or turn a completed mutation into a client error.
/// </summary>
public sealed class PostOutcomeAuditTests
{
    private static Task Throwing(Exception error) => Task.FromException(error);

    // A/B/E — a physical failure is being reported; the audit must not replace it.
    // The caller rethrows after RunAsync returns, so "returns normally" is exactly
    // the property that preserves the original exception.
    [Fact]
    public async Task An_audit_cancellation_is_swallowed_so_the_caller_can_rethrow_the_physical_failure()
    {
        Exception? logged = null;

        await PostOutcomeAudit.RunAsync(_ => Throwing(new OperationCanceledException()), e => logged = e);

        Assert.IsType<OperationCanceledException>(logged);
    }

    [Fact]
    public async Task A_task_cancellation_is_also_swallowed()
    {
        Exception? logged = null;

        await PostOutcomeAudit.RunAsync(_ => Throwing(new TaskCanceledException()), e => logged = e);

        Assert.IsAssignableFrom<OperationCanceledException>(logged);
    }

    // C/D — physical success followed by any audit failure still returns success,
    // because RunAsync never propagates.
    [Theory]
    [MemberData(nameof(AuditFailures))]
    public async Task Every_audit_failure_is_swallowed_and_reported(Exception failure)
    {
        Exception? logged = null;

        await PostOutcomeAudit.RunAsync(_ => Throwing(failure), e => logged = e);

        Assert.Same(failure, logged);
    }

    public static TheoryData<Exception> AuditFailures() =>
    [
        new OperationCanceledException(),
        new TaskCanceledException(),
        new InvalidOperationException("audit store down"),
        new TimeoutException(),
        new RdsExecutionException("SOMETHING", "unexpected"),
    ];

    // F — the audit runs at most once and never re-invokes the physical operation.
    [Fact]
    public async Task The_audit_is_attempted_exactly_once_and_is_never_retried()
    {
        var attempts = 0;

        await PostOutcomeAudit.RunAsync(
            _ => { attempts++; return Throwing(new InvalidOperationException()); },
            _ => { });

        Assert.Equal(1, attempts);
    }

    // The write must not inherit the request token — that is the token most likely to
    // already be cancelled at this point.
    [Fact]
    public async Task The_audit_write_gets_its_own_live_token()
    {
        CancellationToken observed = default;

        await PostOutcomeAudit.RunAsync(
            token => { observed = token; return Task.CompletedTask; },
            _ => Assert.Fail("a successful write must not report a failure"));

        Assert.True(observed.CanBeCanceled, "the write is bounded, not CancellationToken.None");
        Assert.False(observed.IsCancellationRequested, "the write starts from a live token");
    }

    [Fact]
    public async Task A_successful_audit_reports_no_failure()
    {
        var failures = 0;

        await PostOutcomeAudit.RunAsync(_ => Task.CompletedTask, _ => failures++);

        Assert.Equal(0, failures);
    }

    // Even a broken logger must not be able to change the physical outcome.
    [Fact]
    public async Task A_failing_failure_logger_still_cannot_escape()
    {
        await PostOutcomeAudit.RunAsync(
            _ => Throwing(new InvalidOperationException("audit")),
            _ => throw new InvalidOperationException("logger is down too"));
    }
}
