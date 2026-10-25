using Acme.EntityFrameworkCore.Tenancy;

namespace ShopApi;

/// <summary>
/// Reads the tenant from the "tenant_id" claim of the signed-in user.
/// The claim must come from the identity provider (signed token), never from a header
/// or query parameter that the client can set freely.
/// </summary>
public sealed class HttpTenantProvider : ITenantProvider
{
    /// <summary>Claim type that carries the tenant ID.</summary>
    public const string ClaimType = "tenant_id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Creates the provider.</summary>
    /// <param name="httpContextAccessor">Access to the current request.</param>
    public HttpTenantProvider(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Determined anew on every access. This makes the provider work as a singleton too,
    /// e.g. with AddDbContextPool (chapters "Public API and integration into the application"
    /// and "Using the plugin in ASP.NET Core" of the handbook).
    /// </summary>
    public TenantId? CurrentTenantId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimType)?.Value;
            // No or malformed claim: no tenant. RLS then shows no rows (fail closed).
            return Guid.TryParse(value, out var id) ? new TenantId(id) : null;
        }
    }
}
