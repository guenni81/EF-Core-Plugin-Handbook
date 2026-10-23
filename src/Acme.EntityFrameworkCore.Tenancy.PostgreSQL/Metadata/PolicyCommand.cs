// Deliberately in the EF namespace: model snapshots always import Microsoft.EntityFrameworkCore,
// so a generated call like p.For(PolicyCommand.Select) compiles without an extra using.
namespace Microsoft.EntityFrameworkCore;

/// <summary>Command a policy applies to (FOR ALL | SELECT | INSERT | UPDATE | DELETE).</summary>
public enum PolicyCommand
{
    /// <summary>FOR ALL: every command (the default).</summary>
    All,

    /// <summary>FOR SELECT: only USING is allowed.</summary>
    Select,

    /// <summary>FOR INSERT: only WITH CHECK is allowed.</summary>
    Insert,

    /// <summary>FOR UPDATE: USING for the old row, WITH CHECK for the new one.</summary>
    Update,

    /// <summary>FOR DELETE: only USING is allowed.</summary>
    Delete,
}
