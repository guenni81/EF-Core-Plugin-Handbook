using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Migrations.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// Decorator of a second, fictional audit plugin: instead of replacing
/// <see cref="IMigrationsSqlGenerator"/>, it wraps whatever generator is registered (the
/// provider's or another plugin's), delegates to it and appends its own commands. This way
/// it composes with other plugins without any code in the application (chapter "Registering
/// services").
/// </summary>
public class AuditMigrationsSqlGenerator : IMigrationsSqlGenerator
{
    private readonly IMigrationsSqlGenerator _inner;
    private readonly MigrationsSqlGeneratorDependencies _dependencies;

    /// <summary>Creates the decorator around the previously registered generator.</summary>
    /// <param name="inner">The generator that was registered before this plugin.</param>
    /// <param name="dependencies">EF's parameter object, used to build the extra commands.</param>
    public AuditMigrationsSqlGenerator(
        IMigrationsSqlGenerator inner, MigrationsSqlGeneratorDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(dependencies);

        _inner = inner;
        _dependencies = dependencies;
    }

    /// <summary>The wrapped generator; exposed for tests.</summary>
    public IMigrationsSqlGenerator Inner => _inner;

    /// <summary>Generates the inner SQL unchanged, then an audit trigger per new table.</summary>
    public IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
    {
        var commands = _inner.Generate(operations, model, options).ToList();   // unchanged
        var builder = new MigrationCommandListBuilder(_dependencies);
        var sql = _dependencies.SqlGenerationHelper;

        // Appended at the end: by then every table of this migration exists
        foreach (var table in operations.OfType<CreateTableOperation>())
        {
            builder.Append("CREATE TRIGGER audit_changes ")
                .Append("AFTER INSERT OR UPDATE OR DELETE ON ")
                .Append(sql.DelimitIdentifier(table.Name, table.Schema))
                .Append(" FOR EACH ROW EXECUTE FUNCTION audit.log_change()")
                .AppendLine(sql.StatementTerminator)
                .EndCommand();
        }

        commands.AddRange(builder.GetCommandList());
        return commands;
    }
}

/// <summary>Options extension of the fictional audit plugin.</summary>
public class AuditOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>
    /// The decorator trick: look up the generator registered so far, replace its descriptor
    /// with one that creates the original implementation and wraps it in the decorator.
    /// </summary>
    public void ApplyServices(IServiceCollection services)
    {
        // EF calls ApplyServices in the order the extensions were added, so the provider's
        // generator (or that of a plugin activated earlier) is already registered here.
        var inner = services.LastOrDefault(d => d.ServiceType == typeof(IMigrationsSqlGenerator))
            ?? throw new InvalidOperationException(
                "UseContosoAudit() must be called after the database provider, e.g. UseNpgsql().");

        services.Replace(ServiceDescriptor.Describe(
            typeof(IMigrationsSqlGenerator),
            sp => new AuditMigrationsSqlGenerator(
                (IMigrationsSqlGenerator)CreateInstance(sp, inner),
                sp.GetRequiredService<MigrationsSqlGeneratorDependencies>()),
            inner.Lifetime));     // keep the lifetime of the original registration
    }

    /// <summary>Creates the implementation the way the original descriptor would have.</summary>
    private static object CreateInstance(IServiceProvider sp, ServiceDescriptor descriptor)
        => descriptor.ImplementationInstance
           ?? descriptor.ImplementationFactory?.Invoke(sp)
           ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(IDbContextOptionsExtension extension)
            : base(extension)
        {
        }

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using ContosoAudit ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["ContosoAudit"] = "1";
    }
}

/// <summary>Use method of the fictional audit plugin.</summary>
public static class AuditDbContextOptionsBuilderExtensions
{
    /// <summary>Activates the audit plugin; call it after the database provider.</summary>
    public static DbContextOptionsBuilder UseContosoAudit(this DbContextOptionsBuilder builder)
    {
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
            builder.Options.FindExtension<AuditOptionsExtension>() ?? new AuditOptionsExtension());
        return builder;
    }
}

/// <summary>
/// Combining two plugins that both need <see cref="IMigrationsSqlGenerator"/>: the tenancy
/// plugin replaces it, the audit plugin decorates whatever it finds.
/// </summary>
public class CompositionTests
{
    [Fact]
    public void Decorator_wraps_the_generator_of_the_plugin_activated_before()
    {
        using var ctx = Contexts.Create(configure: o => o.UseContosoAudit());

        var generator = Assert.IsType<AuditMigrationsSqlGenerator>(
            ctx.GetService<IMigrationsSqlGenerator>());
        Assert.IsType<TenancyMigrationsSqlGenerator>(generator.Inner);
    }

    [Fact]
    public void Combined_plugins_emit_sql_of_both()
    {
        using var ctx = Contexts.Create(configure: o => o.UseContosoAudit());

        var sql = Contexts.MigrationSql(null, ctx);

        Assert.Contains(sql, s => s.StartsWith("CREATE POLICY tenant_isolation ON \"Orders\""));
        Assert.Contains(sql, s => s.StartsWith("CREATE TRIGGER audit_changes")
                                  && s.Contains("ON \"Orders\""));
    }

    [Fact]
    public void Decorator_requires_the_provider_to_be_registered_first()
    {
        var builder = new DbContextOptionsBuilder<TestContext>();
        builder.UseContosoAudit();                              // too early: no provider yet
        builder.UseNpgsql("Host=localhost;Database=unused");

        Assert.Throws<InvalidOperationException>(() => new TestContext(builder.Options, null));
    }
}
