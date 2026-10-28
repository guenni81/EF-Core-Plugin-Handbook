using Microsoft.EntityFrameworkCore.Design;

// Loads the plugin's design-time services for "dotnet ef". The tooling reads this attribute
// only from the startup and the migrations assembly, never from referenced packages, so the
// application declares it once (chapter "Design-time services"). The second argument limits
// it to contexts that use Npgsql.
[assembly: DesignTimeServicesReference(
    "Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Design.TenancyDesignTimeServices, "
        + "Acme.EntityFrameworkCore.Tenancy.PostgreSQL",
    "Npgsql.EntityFrameworkCore.PostgreSQL")]
