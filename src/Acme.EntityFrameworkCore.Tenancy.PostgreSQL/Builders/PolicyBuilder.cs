using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Builders;

/// <summary>
/// Fluent builder for a policy. Checks PostgreSQL's rules while the model is built,
/// so mistakes surface at startup and not only when the migration is executed.
/// </summary>
/// <remarks>
/// USING and WITH CHECK are both optional, as in CREATE POLICY. Without either, a permissive
/// policy makes no row visible or writable (it adds nothing to the OR of the permissive
/// policies), a restrictive one restricts nothing. Scaffolding reproduces such policies, so
/// the builder accepts them.
/// </remarks>
public sealed class PolicyBuilder
{
    private readonly string _name;
    private PolicyCommand _command = PolicyCommand.All;
    private bool _permissive = true;
    private readonly List<string> _roles = [];
    private string? _using;
    private string? _withCheck;

    internal PolicyBuilder(string name) => _name = name;

    /// <summary>Command the policy applies to (FOR ...). Default: <see cref="PolicyCommand.All"/>.</summary>
    /// <param name="command">The command.</param>
    /// <returns>The same builder for chaining.</returns>
    public PolicyBuilder For(PolicyCommand command) { _command = command; return this; }

    /// <summary>
    /// Makes the policy RESTRICTIVE: it is combined with AND instead of OR with the other
    /// policies. Default is PERMISSIVE.
    /// </summary>
    /// <returns>The same builder for chaining.</returns>
    public PolicyBuilder Restrictive() { _permissive = false; return this; }

    /// <summary>Roles the policy applies to (TO ...). Without roles it applies to PUBLIC.</summary>
    /// <param name="roles">Role names; repeated calls add to the list.</param>
    /// <returns>The same builder for chaining.</returns>
    public PolicyBuilder To(params string[] roles) { _roles.AddRange(roles); return this; }

    /// <summary>SQL expression for USING. NOT escaped (like HasComputedColumnSql).</summary>
    /// <param name="sql">Boolean SQL expression that filters existing rows.</param>
    /// <returns>The same builder for chaining.</returns>
    public PolicyBuilder Using(string sql) { _using = sql; return this; }

    /// <summary>SQL expression for WITH CHECK. NOT escaped.</summary>
    /// <param name="sql">Boolean SQL expression that new or changed rows must satisfy.</param>
    /// <returns>The same builder for chaining.</returns>
    public PolicyBuilder WithCheck(string sql) { _withCheck = sql; return this; }

    internal RlsPolicy Build()
    {
        // The same rules PostgreSQL enforces in CREATE POLICY, checked early; both clauses
        // are optional
        if (_command == PolicyCommand.Insert && _using is not null)
            throw new InvalidOperationException($"INSERT policy '{_name}' only allows WITH CHECK.");

        if (_command is PolicyCommand.Select or PolicyCommand.Delete && _withCheck is not null)
            throw new InvalidOperationException($"{_command} policy '{_name}' only allows USING.");

        // null instead of an empty array: no roles means "no TO clause" (PUBLIC)
        return new RlsPolicy(
            _name, _command, _permissive,
            _roles.Count > 0 ? [.. _roles] : null,
            _using, _withCheck);
    }
}
