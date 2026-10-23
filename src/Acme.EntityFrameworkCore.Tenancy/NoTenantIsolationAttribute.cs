namespace Acme.EntityFrameworkCore.Tenancy;

/// <summary>
/// Data annotation: excludes an <see cref="ITenantOwned"/> entity from automatic isolation
/// (e.g. for cross-tenant auditing). Equivalent to <c>HasNoRowLevelSecurity()</c> in the
/// Fluent API, which takes precedence.
/// </summary>
/// <remarks>
/// The opt-out is logged as the warning <c>TenancyEventId.TenantIsolationOptedOut</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class NoTenantIsolationAttribute : Attribute;
