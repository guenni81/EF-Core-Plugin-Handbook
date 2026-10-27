using Acme.EntityFrameworkCore.Tenancy.SqlServer.Metadata;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.SqlServer.Design.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Tests;

/// <summary>Session key and policy schema as recorded state of the model.</summary>
internal static class Snapshots
{
    /// <summary>
    /// The model as a snapshot of version 1.0 records it: tenant column on the entity type, but no
    /// session key and no policy schema on the model.
    /// </summary>
    public static IModel Legacy(DbContext context)
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 128);
        SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

        modelBuilder.Entity("Acme.EntityFrameworkCore.Tenancy.SqlServer.Tests.Invoice", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("int");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<int>("Id"));
            b.Property<Guid>("TenantId").HasColumnType("uniqueidentifier");
            b.HasKey("Id");
            b.HasIndex("TenantId");
            b.ToTable("Invoice");
            b.HasAnnotation(SqlServerRlsAnnotationNames.TenantColumn, "TenantId");
        });

        return context.GetService<IModelRuntimeInitializer>()
            .Initialize(modelBuilder.FinalizeModel(), designTime: true, validationLogger: null);
    }
}

/// <summary>Migration state of the session configuration, without a database.</summary>
public class SqlServerSessionSettingsModelTests
{
    private static IModel Design(MappingContext context) => context.GetService<IDesignTimeModel>().Model;

    [Fact]
    public void Model_records_session_key_and_policy_schema()
    {
        using var context = MappingContext.Create(Versions.V1, sessionKey: "NewTenant", policySchema: "rls");

        Assert.Equal("NewTenant", Design(context)[SqlServerRlsAnnotationNames.SessionKey]);
        Assert.Equal("rls", Design(context)[SqlServerRlsAnnotationNames.PolicySchema]);
    }

    [Fact]
    public void Unchanged_model_produces_no_operations()
    {
        using var source = MappingContext.Create(Versions.V1);

        Assert.Empty(Versions.Operations(Design(source), MappingContext.Create(Versions.V1)));
    }

    [Fact]
    public void Changed_session_key_is_detected_and_rebuilds_function_and_policies()
    {
        using var source = MappingContext.Create(Versions.V1);
        var target = MappingContext.Create(Versions.V1, sessionKey: "NewTenant");

        var operation = Assert.IsType<AlterDatabaseOperation>(
            Assert.Single(Versions.Operations(Design(source), target)));
        Assert.Equal("TenantId", operation.OldDatabase[SqlServerRlsAnnotationNames.SessionKey]);
        Assert.Equal("NewTenant", operation[SqlServerRlsAnnotationNames.SessionKey]);

        var sql = Versions.Commands(source, MappingContext.Create(Versions.V1, sessionKey: "NewTenant"))
            .Select(c => c.CommandText).ToList();
        Assert.Contains("DROP SECURITY POLICY", sql[0], StringComparison.Ordinal);
        Assert.StartsWith("DROP FUNCTION IF EXISTS [acme].[fn_tenant_predicate]", sql[1], StringComparison.Ordinal);
        Assert.Contains(sql, s => s.Contains("SESSION_CONTEXT(N''NewTenant'')", StringComparison.Ordinal));
        Assert.StartsWith("CREATE SECURITY POLICY [acme].[tenant_dbo_Invoice]", sql[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Historical_migration_uses_its_own_model_not_the_current_options()
    {
        // The application now runs with "NewTenant"; the migration's target model recorded "TenantId"
        using var historical = MappingContext.Create(Versions.V1);
        using var current = MappingContext.Create(Versions.V1, sessionKey: "NewTenant");

        var sql = current.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsSqlGenerator>()
            .Generate(Versions.Operations(null, MappingContext.Create(Versions.V1)), Design(historical))
            .Select(c => c.CommandText).ToList();

        Assert.Contains(sql, s => s.Contains("SESSION_CONTEXT(N''TenantId'')", StringComparison.Ordinal));
        Assert.DoesNotContain(sql, s => s.Contains("NewTenant", StringComparison.Ordinal));
    }

    [Fact]
    public void Snapshot_before_1_1_only_verifies_the_existing_function()
    {
        using var context = MappingContext.Create(Versions.V1);
        var legacy = Snapshots.Legacy(context);

        var operation = Assert.IsType<AlterDatabaseOperation>(
            Assert.Single(Versions.Operations(legacy, MappingContext.Create(Versions.V1))));
        Assert.Null(operation.OldDatabase[SqlServerRlsAnnotationNames.SessionKey]);

        // Old values unknown: nothing is dropped, an existing function must read "TenantId"
        var sql = Versions.Commands(legacy, MappingContext.Create(Versions.V1)).Select(c => c.CommandText).ToList();
        var check = Assert.Single(sql);
        Assert.Contains("IS NOT NULL AND OBJECT_DEFINITION", check, StringComparison.Ordinal);
        Assert.Contains("SESSION[_]CONTEXT(N''TenantId'')", check, StringComparison.Ordinal);
        Assert.Contains("THROW 50000", check, StringComparison.Ordinal);
    }

    [Fact]
    public void Down_to_a_snapshot_before_1_1_leaves_function_and_policies_alone()
    {
        using var context = MappingContext.Create(Versions.V1);
        var legacy = Snapshots.Legacy(context);
        using var current = MappingContext.Create(Versions.V1);

        var sql = context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsSqlGenerator>()
            .Generate(context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsModelDiffer>()
                .GetDifferences(Design(current).GetRelationalModel(), legacy.GetRelationalModel()), legacy)
            .Select(c => c.CommandText).ToList();

        Assert.DoesNotContain(sql, s => s.Contains("SECURITY POLICY", StringComparison.Ordinal)
                                        || s.Contains("FUNCTION", StringComparison.Ordinal));
    }

    [Fact]
    public void Snapshot_records_session_key_and_policy_schema()
    {
        using var context = MappingContext.Create(Versions.V1, sessionKey: "NewTenant", policySchema: "rls");
        var services = new ServiceCollection().AddEntityFrameworkDesignTimeServices();
        new SqlServerDesignTimeServices().ConfigureDesignTimeServices(services);
        services.AddDbContextDesignTimeServices(context);
        using var provider = services.BuildServiceProvider();

        var snapshot = provider.GetRequiredService<IMigrationsCodeGeneratorSelector>().Select("C#")
            .GenerateSnapshot("Shop", typeof(MappingContext), "ShopSnapshot", Design(context));

        Assert.Contains("\"Acme:SqlServerRls:SessionKey\", \"NewTenant\"", snapshot, StringComparison.Ordinal);
        Assert.Contains("\"Acme:SqlServerRls:PolicySchema\", \"rls\"", snapshot, StringComparison.Ordinal);
    }
}

/// <summary>Changing the session configuration against a real SQL Server.</summary>
public class SqlServerSessionSettingsTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture;

    public SqlServerSessionSettingsTests(SqlServerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private static Task MigrateAsync(string owner, MappingContext? source, MappingContext target)
        => SqlServerMigrationTests.RunAsync(owner, Versions.Commands(source, target));

    /// <summary>Writes and reads through EF with the plugin's runtime interceptors.</summary>
    private static async Task AssertAppIsolatedAsync(string app, string sessionKey)
    {
        TenantId a = TenantId.New(), b = TenantId.New();
        foreach (var tenant in new[] { a, b })
        {
            await using var db = MappingContext.Create(Versions.V1, app, sessionKey,
                tenants: new MutableTenantProvider { CurrentTenantId = tenant });
            db.Add(new Invoice());
            await db.SaveChangesAsync();
        }

        await using var reader = MappingContext.Create(Versions.V1, app, sessionKey,
            tenants: new MutableTenantProvider { CurrentTenantId = a });
        var rows = await reader.Set<Invoice>().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, i => Assert.Equal(a, i.TenantId));
    }

    [Fact]
    public async Task Changed_session_key_is_migrated_up_and_down()
    {
        var (owner, app) = await SqlServerMigrationTests.CreateDatabaseAsync(_fixture);
        await MigrateAsync(owner, null, MappingContext.Create(Versions.V1));
        await AssertAppIsolatedAsync(app, "TenantId");

        await MigrateAsync(owner, MappingContext.Create(Versions.V1),
            MappingContext.Create(Versions.V1, sessionKey: "NewTenant"));

        // The app sets and the database reads the same key; the old key opens nothing
        await AssertAppIsolatedAsync(app, "NewTenant");
        await SqlServerMigrationTests.AssertIsolatedAsync(app, "[dbo].[Invoice]", "TenantId", "NewTenant");
        await using (var connection = new SqlConnection(app))
        {
            await connection.OpenAsync();
            await SqlServerMigrationTests.Execute(connection,
                "EXEC sp_set_session_context @key = N'TenantId', @value = @t", ("@t", Guid.NewGuid()));
            Assert.Equal(0, await SqlServerMigrationTests.Execute(connection, "SELECT count(*) FROM [Invoice]"));
        }

        // Down
        await MigrateAsync(owner, MappingContext.Create(Versions.V1, sessionKey: "NewTenant"),
            MappingContext.Create(Versions.V1));
        await AssertAppIsolatedAsync(app, "TenantId");
    }

    [Fact]
    public async Task Changed_policy_schema_moves_function_and_policies()
    {
        var (owner, app) = await SqlServerMigrationTests.CreateDatabaseAsync(_fixture);
        await MigrateAsync(owner, null, MappingContext.Create(Versions.V1));

        await MigrateAsync(owner, MappingContext.Create(Versions.V1),
            MappingContext.Create(Versions.V1, policySchema: "rls"));

        await AssertAppIsolatedAsync(app, "TenantId");
        await using var connection = new SqlConnection(owner);
        await connection.OpenAsync();
        Assert.Equal(1, await SqlServerMigrationTests.Execute(connection,
            "SELECT count(*) FROM sys.objects WHERE name = N'fn_tenant_predicate'"));
        Assert.Equal("rls", await SqlServerMigrationTests.Execute(connection,
            "SELECT SCHEMA_NAME(schema_id) FROM sys.security_policies"));
    }

    [Fact]
    public async Task Installation_from_1_0_is_upgraded_and_then_changes_its_session_key()
    {
        var (owner, app) = await SqlServerMigrationTests.CreateDatabaseAsync(_fixture);
        await using (var context = MappingContext.Create(Versions.V1))
        {
            // The database as version 1.0 left it: function with "TenantId", policy tenant_Invoice
            await MigrateAsync(owner, null, MappingContext.Create(Versions.V1));
            await using var connection = new SqlConnection(owner);
            await connection.OpenAsync();
            await SqlServerMigrationTests.Execute(connection, "DROP SECURITY POLICY [acme].[tenant_dbo_Invoice]");
            await SqlServerMigrationTests.Execute(connection,
                "CREATE SECURITY POLICY [acme].[tenant_Invoice] "
                + "ADD FILTER PREDICATE [acme].[fn_tenant_predicate]([TenantId]) ON [dbo].[Invoice] WITH (STATE = ON)");

            // Changing the key in the first migration after the upgrade is rejected: the old key
            // is not recorded, so the function cannot be proven to be the plugin's
            var ex = await Assert.ThrowsAsync<SqlException>(() => SqlServerMigrationTests.RunAsync(owner,
                Versions.Commands(Snapshots.Legacy(context), MappingContext.Create(Versions.V1, sessionKey: "NewTenant"))));
            Assert.Contains("reads a different session key", ex.Message, StringComparison.Ordinal);

            // Upgrade first, then change the key in a migration of its own
            await SqlServerMigrationTests.RunAsync(owner,
                Versions.Commands(Snapshots.Legacy(context), MappingContext.Create(Versions.V1)));
            await AssertAppIsolatedAsync(app, "TenantId");
            await MigrateAsync(owner, MappingContext.Create(Versions.V1),
                MappingContext.Create(Versions.V1, sessionKey: "NewTenant"));
        }

        await AssertAppIsolatedAsync(app, "NewTenant");
        await SqlServerMigrationTests.AssertIsolatedAsync(app, "[dbo].[Invoice]", "TenantId", "NewTenant");
    }

    [Fact]
    public async Task Second_context_with_another_session_key_in_the_same_schema_is_rejected()
    {
        var (owner, _) = await SqlServerMigrationTests.CreateDatabaseAsync(_fixture);
        await MigrateAsync(owner, null, MappingContext.Create(Versions.V1));

        // Another context of the same database, configured with another key
        var other = MappingContext.Create(m => m.Entity<ArchivedInvoice>(), sessionKey: "OtherTenant");
        var ex = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(owner, null, other));
        Assert.Contains("reads a different session key", ex.Message, StringComparison.Ordinal);

        // With its own policy schema it gets its own function
        await MigrateAsync(owner, null,
            MappingContext.Create(m => m.Entity<ArchivedInvoice>(), sessionKey: "OtherTenant", policySchema: "other"));
    }
}
