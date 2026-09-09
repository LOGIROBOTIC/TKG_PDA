namespace TkgPda.Application;

public enum RdsRobotMatchKind
{
    /// <summary>No robot in the current fleet answers to the alias.</summary>
    NoMatch,

    /// <summary>Exactly one physical robot answers to the alias.</summary>
    Unique,

    /// <summary>
    /// More than one PHYSICAL robot answers to the alias. Always fail closed: there
    /// is no safe way to guess which machine the caller meant.
    /// </summary>
    Ambiguous
}

public readonly record struct RdsRobotMatch(RdsRobotMatchKind Kind, RdsCoreRobotTelemetry? Robot)
{
    public static readonly RdsRobotMatch NoMatch = new(RdsRobotMatchKind.NoMatch, null);
    public static readonly RdsRobotMatch Ambiguous = new(RdsRobotMatchKind.Ambiguous, null);
    public static RdsRobotMatch Unique(RdsCoreRobotTelemetry robot) => new(RdsRobotMatchKind.Unique, robot);
}

/// <summary>
/// The one place an alias is turned into a physical robot.
///
/// SEER exposes two identity spaces and an alias may legitimately be written in
/// either, so matching has to search both. That creates a collision the callers used
/// to resolve by taking the first array entry: alias "X" can be robot A's
/// <c>uuid</c> AND robot B's <c>vehicle_id</c>, and <c>FirstOrDefault</c> then picked
/// whichever RDSCore happened to list first — so the same request could target a
/// different physical machine depending on payload order. For a motion-enabling
/// mutation that is the worst possible failure mode.
///
/// The distinction that matters is PHYSICAL, not textual:
///   one robot with uuid == vehicle_id == "X"        -> one machine  -> Unique
///   robot A uuid "X" and robot B vehicle_id "X"     -> two machines -> Ambiguous
///
/// Distinctness is therefore keyed on the whole identity pair rather than on a single
/// resolved string: a lease key alone would collapse (uuid=X, vehicle=null) and
/// (uuid=null, vehicle=X) onto the same value and hide exactly the ambiguity this
/// type exists to catch.
/// </summary>
public static class RdsRobotResolver
{
    public static RdsRobotMatch Resolve(IReadOnlyList<RdsCoreRobotTelemetry> robots, string? alias)
    {
        ArgumentNullException.ThrowIfNull(robots);
        if (string.IsNullOrWhiteSpace(alias)) return RdsRobotMatch.NoMatch;

        var matches = robots.Where(robot => robot.ResolvedIdentity.Matches(alias)).ToArray();
        if (matches.Length == 0) return RdsRobotMatch.NoMatch;

        // The same physical robot appearing twice in one payload is still one robot;
        // two different identity pairs are two robots.
        var distinctMachines = matches
            .Select(robot => (
                Uuid: Normalize(robot.ResolvedIdentity.Uuid),
                Vehicle: Normalize(robot.ResolvedIdentity.VehicleId)))
            .Distinct()
            .Count();

        return distinctMachines == 1 ? RdsRobotMatch.Unique(matches[0]) : RdsRobotMatch.Ambiguous;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
