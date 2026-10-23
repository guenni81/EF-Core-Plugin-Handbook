namespace Acme.EntityFrameworkCore.Tenancy.Metadata;

/// <summary>Provider-independent annotations of the plugin.</summary>
public static class TenancyAnnotationNames
{
    /// <summary>
    /// Common prefix of all annotations of this plugin. A unique prefix avoids collisions with
    /// EF, providers and other plugins.
    /// </summary>
    public const string Prefix = "Acme:Tenancy:";

    /// <summary>
    /// On the root entity type: name of the tenant property. Needed at RUN TIME
    /// (SaveChanges interceptor) and therefore also ends up in the compiled model.
    /// </summary>
    public const string TenantProperty = Prefix + "TenantProperty";

    /// <summary>On a property: explicitly marked as the tenant property.</summary>
    public const string IsTenantProperty = Prefix + "IsTenantProperty";
}
