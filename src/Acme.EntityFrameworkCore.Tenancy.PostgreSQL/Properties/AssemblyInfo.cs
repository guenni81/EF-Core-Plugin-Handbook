using Microsoft.EntityFrameworkCore.Design;

// Loads the design-time services automatically as soon as the startup project references
// this assembly - but only if the context uses Npgsql (second argument).
// The type is given as a string: the tooling resolves it via reflection at design time.
[assembly: DesignTimeServicesReference(
    "Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Design.TenancyDesignTimeServices, "
        + "Acme.EntityFrameworkCore.Tenancy.PostgreSQL",
    "Npgsql.EntityFrameworkCore.PostgreSQL")]
