using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// Database.ApplyTenantSession(): the explicit counterpart to the session interceptor for
/// connections EF does not open itself (chapter 15). Behavior against a real database is
/// covered by the integration tests.
/// </summary>
public class ApplyTenantSessionTests
{
    private sealed class FixedTenantProvider : ITenantProvider
    {
        public TenantId? CurrentTenantId { get; } = TenantId.New();
    }

    [Fact]
    public void Throws_without_session_interceptor()
    {
        // UseAcmeTenancy() without application services registers no interceptors
        using var ctx = Contexts.Create();

        Assert.Throws<TenancyException>(() => ctx.Database.ApplyTenantSession());
    }

    [Fact]
    public void Does_nothing_for_a_closed_connection()
    {
        using var ctx = Contexts.Create(configure: o => o.AddInterceptors(
            new TenantConnectionInterceptor(new FixedTenantProvider())));

        // Closed: EF opens it later and the interceptor sets the tenant then
        Assert.False(ctx.Database.ApplyTenantSession());
    }
}
