namespace TkgPda.Application;

/// <summary>
/// Single normalization boundary for the customer employee / registry number
/// (sicil numarası). The value is always treated as a STRING: whitespace is
/// trimmed at the boundaries, but significant characters and leading zeros are
/// preserved ("001245" stays "001245" and never collapses to "1245").
/// Comparison and uniqueness are trim + case-insensitive, matching the database
/// index <c>uq_users_employee_no_normalized</c> on <c>UPPER(BTRIM(employee_no))</c>.
/// </summary>
public static class EmployeeNumber
{
    /// <summary>
    /// Canonical stored/display representation: the trimmed value, or
    /// <c>null</c> when the input is missing or whitespace-only. Case and
    /// leading zeros are preserved.
    /// </summary>
    public static string? Canonicalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Case-insensitive lookup key used for equality against the database
    /// (<c>UPPER(BTRIM(employee_no))</c>), or <c>null</c> for a blank value.
    /// </summary>
    public static string? NormalizeForLookup(string? value) =>
        Canonicalize(value)?.ToUpperInvariant();
}
