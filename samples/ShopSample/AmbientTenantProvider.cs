using Acme.EntityFrameworkCore.Tenancy;

namespace ShopSample;

/// <summary>
/// Simple tenant provider based on AsyncLocal. In ASP.NET Core the tenant typically comes
/// from a claim of the current request (see the ShopApi sample).
/// </summary>
public sealed class AmbientTenantProvider : ITenantProvider
{
    // AsyncLocal flows with the async control flow, so concurrent operations each keep
    // their own tenant even though the provider itself is shared.
    private static readonly AsyncLocal<TenantId?> Current = new();

    /// <summary>The tenant set by the innermost active <see cref="Use"/> scope.</summary>
    public TenantId? CurrentTenantId => Current.Value;

    /// <summary>Sets the tenant until the returned scope is disposed.</summary>
    /// <param name="tenant">Tenant to activate.</param>
    /// <returns>A scope that restores the previous tenant on dispose (scopes can be nested).</returns>
    public static IDisposable Use(TenantId tenant)
    {
        var previous = Current.Value;
        Current.Value = tenant;
        return new Restore(previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly TenantId? _previous;

        public Restore(TenantId? previous) => _previous = previous;

        public void Dispose() => Current.Value = _previous;
    }
}
