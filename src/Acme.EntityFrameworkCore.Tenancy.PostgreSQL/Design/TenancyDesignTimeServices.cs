using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Design.Internal;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Scaffolding;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Design;

/// <summary>
/// Design-time services for "dotnet ef". Loaded automatically via
/// [DesignTimeServicesReference] in Properties/AssemblyInfo.cs.
/// </summary>
/// <remarks>
/// The registered generators derive from Npgsql types in *.Internal namespaces and the
/// interface <see cref="ICSharpRuntimeAnnotationCodeGenerator"/> lives in
/// Microsoft.EntityFrameworkCore.Design.Internal: internal EF/Npgsql APIs (EF1001) that
/// may change in any release.
/// </remarks>
public class TenancyDesignTimeServices : IDesignTimeServices
{
    /// <summary>Replaces the EF/Npgsql design-time services with the tenancy-aware variants.</summary>
    /// <param name="services">The design-time service collection.</param>
    public void ConfigureDesignTimeServices(IServiceCollection services)
    {
        // Remove all registrations (EF default, Npgsql) each time, then add ours:
        // independent of the order in which the tooling loads services.

        // Snapshot and scaffolding: annotations as Fluent API (chapter "Design-time services")
        services.RemoveAll<IAnnotationCodeGenerator>();
        services.AddSingleton<IAnnotationCodeGenerator, TenancyAnnotationCodeGenerator>();

        // Compiled models: filter out design-time annotations
        // (chapter "Property-level Fluent API and runtime annotations")
        services.RemoveAll<ICSharpRuntimeAnnotationCodeGenerator>();
        services.AddSingleton<ICSharpRuntimeAnnotationCodeGenerator, TenancyCSharpRuntimeAnnotationCodeGenerator>();

        // Scaffolding: read RLS and policies from the database (chapter "Scaffolding in practice")
        services.RemoveAll<IDatabaseModelFactory>();
        services.AddSingleton<IDatabaseModelFactory, TenancyDatabaseModelFactory>();
    }
}
