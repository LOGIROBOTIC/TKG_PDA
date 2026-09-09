namespace TkgPda.Application;

/// <summary>
/// Safety classification of an RDS operation with respect to physical work.
/// This is the single definition of what the global dispatch gate means; no
/// adapter or endpoint may hold a second, contradictory notion of physical
/// permission.
/// </summary>
public enum RdsMutationClass
{
    /// <summary>Reads only. Never gated.</summary>
    ReadOnly,

    /// <summary>
    /// Stops or pauses work already under way. Permitted even with the gate closed,
    /// because it reduces motion rather than causing it (<c>/terminate</c>,
    /// <c>/gotoSitePause</c>, marking a robot undispatchable).
    /// </summary>
    SafetyStop,

    /// <summary>
    /// Starts, resumes, re-enables or advances physical work — directly
    /// (<c>/gotoSiteResume</c>, <c>dispatchable=true</c>) or by removing what was
    /// holding the robot back (<c>/clearRobotAllError</c>, <c>/manualFinished</c>
    /// advancing to the next block, <c>/redoFailedOrder</c> re-running one).
    /// </summary>
    MotionEnabling,

    /// <summary>
    /// Creates or mutates execution identity or ordering such that work can be
    /// dispatched or re-dispatched (<c>/setOrder</c>, WindTask creation,
    /// <c>/setPriority</c>, and the <c>/lock</c> + <c>/switchMap</c> + <c>/unlock</c>
    /// map preparation that precedes a dispatch).
    /// </summary>
    ExecutionMutation
}

/// <summary>
/// The global physical mutation gate. <c>Rds:DispatchEnabled = false</c> means no
/// operation may START, RESUME, RE-ENABLE, ADVANCE or RE-DISPATCH physical work,
/// regardless of which execution engine is configured or which role is calling.
/// Being Admin/Supervisor-only does not by itself make an operation permissible.
/// </summary>
public static class RdsMutationPolicy
{
    public static bool IsAllowed(RdsMutationClass mutationClass, bool globalDispatchEnabled) =>
        mutationClass switch
        {
            RdsMutationClass.ReadOnly => true,
            RdsMutationClass.SafetyStop => true,
            _ => globalDispatchEnabled
        };

    /// <summary>
    /// Classifies a <c>/dispatchable</c> request by its <c>type</c>. Making a robot
    /// dispatchable re-enables it for work; the undispatchable variants remove it
    /// from dispatch and are therefore safety-reducing.
    /// </summary>
    public static RdsMutationClass ClassifyDispatchable(string? type) =>
        (type ?? string.Empty).Trim().ToLowerInvariant() == "dispatchable"
            ? RdsMutationClass.MotionEnabling
            : RdsMutationClass.SafetyStop;

    public const string BlockedCode = "RDS_DISPATCH_DISABLED";

    public static string BlockedMessage(string operation) =>
        $"Physical RDS dispatch is globally disabled by configuration (Rds:DispatchEnabled=false), so '{operation}' is refused because it would start, resume, re-enable or advance physical work.";
}
