using System.Text.Json;

namespace TkgPda.Application;

/// <summary>
/// SEER exposes two distinct robot identity spaces on <c>/robotsStatus</c>:
/// <c>uuid</c> (the internal robot identity) and <c>vehicle_id</c> (the dispatch
/// identifier that the vehicle-scoped RDSCore APIs contract on — <c>/setOrder</c>
/// <c>vehicle</c>, <c>/lock</c>, <c>/unlock</c>, <c>/switchMap</c>,
/// <c>/manualFinished</c>, <c>/redoFailedOrder</c>, <c>/dispatchable</c>).
///
/// The two values are commonly identical in the local simulator and commonly
/// different on real hardware, so neither may be treated as universally
/// canonical. Before this type existed the readers disagreed — one preferred
/// <c>uuid ?? vehicle_id</c> and another <c>vehicle_id ?? uuid</c> — which made
/// lock correlation compare values across identity spaces and silently fail on
/// real robots while passing against the simulator.
///
/// Display identity (model / friendly name) is deliberately NOT modelled here.
/// It is carried separately on the telemetry records so that a display string
/// can never be used as a mutation target.
/// </summary>
public sealed record RdsRobotIdentity(string? Uuid, string? VehicleId)
{
    /// <summary>
    /// The identifier to send to RDSCore when targeting this robot. The
    /// vehicle-scoped RDSCore APIs contract on <c>vehicle_id</c>; <c>uuid</c> is
    /// only a fallback for payloads that omit it. This is also the stable key for
    /// joining robot-scoped records (alarms, counters) read from one snapshot.
    /// </summary>
    public string? MutationTarget => Normalize(VehicleId) ?? Normalize(Uuid);

    /// <summary>
    /// Identifier for the commissioning-discovery operations that key by the
    /// internal robot identity rather than the dispatch identity:
    /// <c>/getRobotParkPoints</c> takes uuid-keyed requests and <c>/queryDist</c>
    /// returns a uuid-keyed map. Choosing per operation contract is deliberate —
    /// neither identity space is universally canonical.
    /// </summary>
    public string? DiscoveryKey => Normalize(Uuid) ?? Normalize(VehicleId);

    public bool HasValue => MutationTarget is not null;

    /// <summary>
    /// True when <paramref name="candidate"/> names this robot in EITHER identity
    /// space. Correlation inputs — audit lock fingerprints, task-template
    /// <c>vehicle</c> values, navigation profiles — may have been recorded in
    /// either space, so a match on either is accepted while a match on neither is
    /// rejected. This never widens to a match on a blank candidate.
    /// </summary>
    public bool Matches(string? candidate)
    {
        var value = Normalize(candidate);
        if (value is null) return false;
        return Equal(VehicleId, value) || Equal(Uuid, value);
    }

    /// <summary>
    /// Reads both identity spaces from one <c>/robotsStatus</c> <c>report[]</c>
    /// element. All robot telemetry readers use this so the three parsers cannot
    /// drift apart again.
    /// </summary>
    public static RdsRobotIdentity FromRobotElement(JsonElement robot) =>
        new(ReadString(robot, "uuid"), ReadString(robot, "vehicle_id"));

    private static bool Equal(string? left, string right) =>
        Normalize(left) is { } value && string.Equals(value, right, StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? Normalize(value.GetString())
            : null;
}
