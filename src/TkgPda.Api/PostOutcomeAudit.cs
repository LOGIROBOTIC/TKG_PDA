namespace TkgPda.Api;

/// <summary>
/// Bookkeeping that runs AFTER a physical outcome is already known, and must never
/// change that outcome.
///
/// Two failure modes are being prevented, both previously real. First, the audit
/// catches were filtered with <c>when (e is not OperationCanceledException)</c>, so a
/// cancelled audit write escaped — replacing the genuine physical exception on the
/// failure path, and converting a mutation that had already reached the robot into a
/// client error on the success path, which invites the caller to retry a physical
/// action. Second, the write used the request's own cancellation token, which is
/// precisely the token most likely to be cancelled at that moment.
///
/// So the write runs under its own short, bounded token — bounded rather than
/// <see cref="CancellationToken.None"/> so a stuck write cannot hang the response —
/// and EVERY exception, cancellation included, is swallowed and reported to the
/// supplied logger. The caller then rethrows the original physical exception, or
/// returns the real physical result, exactly as if the audit had succeeded.
/// </summary>
public static class PostOutcomeAudit
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    public static async Task RunAsync(
        Func<CancellationToken, Task> write,
        Action<Exception> onFailure)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(onFailure);

        try
        {
            using var budget = new CancellationTokenSource(Budget);
            await write(budget.Token);
        }
        catch (Exception auditFailure)
        {
            // Deliberately unfiltered: OperationCanceledException is the case that
            // used to escape and corrupt the reported outcome.
            try
            {
                onFailure(auditFailure);
            }
            catch
            {
                // Even the logging must not be able to change the physical outcome.
            }
        }
    }
}
