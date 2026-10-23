using System.Text.RegularExpressions;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;

/// <summary>
/// Validates identifiers that the plugin itself embeds in SQL. Wherever the plugin composes
/// SQL, it is responsible for valid and harmless values.
/// </summary>
/// <remarks>
/// Allow-list instead of escaping: the values end up in policies, string literals and
/// DDL, where a single escaping rule would not fit every position.
/// </remarks>
internal static partial class TenancyIdentifiers
{
    // Custom settings must contain a dot (PostgreSQL rejects unqualified unknown names)
    [GeneratedRegex(@"^[a-z_][a-z0-9_]*\.[a-z_][a-z0-9_]*$")]
    private static partial Regex SettingNameRegex();

    // 63 bytes = NAMEDATALEN - 1; longer names would be truncated by PostgreSQL
    [GeneratedRegex(@"^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex SimpleIdentifierRegex();

    /// <summary>Returns the value if it is a valid custom setting name, otherwise throws.</summary>
    /// <param name="value">The setting name to check.</param>
    public static string ValidateSettingName(string value)
        => SettingNameRegex().IsMatch(value)
            ? value
            : throw new ArgumentException(
                $"Invalid setting name '{value}'. Expected 'prefix.name' "
                + "(lowercase letters, digits, underscore).", nameof(value));

    /// <summary>Returns the value if it is a simple lowercase identifier, otherwise throws.</summary>
    /// <param name="value">The identifier to check.</param>
    /// <param name="what">What the identifier names, used in the error message.</param>
    public static string ValidateIdentifier(string value, string what)
        => SimpleIdentifierRegex().IsMatch(value)
            ? value
            : throw new ArgumentException(
                $"Invalid {what} '{value}' (lowercase letters, digits, underscore; max. 63).",
                nameof(value));
}
