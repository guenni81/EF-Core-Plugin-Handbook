namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;

/// <summary>Annotation names. Always with a custom prefix to avoid collisions.</summary>
/// <remarks>
/// These strings are written into model snapshots and migrations; renaming them would
/// break existing migrations.
/// </remarks>
public static class RlsAnnotationNames
{
    /// <summary>Common prefix of all RLS annotations.</summary>
    public const string Prefix = "Acme:Rls:";

    /// <summary><see langword="true"/> = RLS enabled; <see langword="false"/> = explicit opt-out.</summary>
    public const string Enabled = Prefix + "Enabled";

    /// <summary>
    /// <see langword="true"/> = FORCE ROW LEVEL SECURITY. Never stored as <see langword="false"/>.
    /// </summary>
    public const string Forced = Prefix + "Forced";

    /// <summary>Prefix of the policy annotations; followed by the policy name.</summary>
    public const string PolicyPrefix = Prefix + "Policy:";

    /// <summary>Key of the optional named query filter.</summary>
    public const string QueryFilterKey = "Acme.Tenancy";
}
