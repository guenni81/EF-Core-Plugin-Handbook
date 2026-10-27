using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Infrastructure;

/// <summary>
/// Makes the tenancy settings available to singleton services of EF's internal service provider
/// (translator, migrations SQL generator, interceptor).
/// </summary>
public interface ISqlServerTenancySingletonOptions : ISingletonOptions
{
    /// <summary>The settings the internal service provider was built with.</summary>
    SqlServerTenancySettings Settings { get; }
}

/// <summary>
/// Default implementation of <see cref="ISqlServerTenancySingletonOptions" />. EF calls
/// <see cref="Initialize" /> once per internal service provider and <see cref="Validate" /> when a
/// context uses a provider it did not build for exactly these options, above all with
/// UseInternalServiceProvider.
/// </summary>
public class SqlServerTenancySingletonOptions : ISqlServerTenancySingletonOptions
{
    /// <inheritdoc />
    public SqlServerTenancySettings Settings { get; private set; } = SqlServerTenancySettings.Default;

    /// <summary>Takes the settings from the options extension, or the defaults if there is none.</summary>
    /// <param name="options">The options the internal service provider is built from.</param>
    public virtual void Initialize(IDbContextOptions options)
        => Settings = options.FindExtension<SqlServerTenancyOptionsExtension>()?.Settings
                      ?? SqlServerTenancySettings.Default;

    /// <summary>
    /// Guards against options that differ from those the singletons were initialized with; this
    /// only happens when the service provider is shared despite different settings.
    /// </summary>
    /// <param name="options">The options of the context being created.</param>
    /// <exception cref="InvalidOperationException">The settings differ.</exception>
    public virtual void Validate(IDbContextOptions options)
    {
        var settings = options.FindExtension<SqlServerTenancyOptionsExtension>()?.Settings
                       ?? SqlServerTenancySettings.Default;

        if (settings != Settings)
            throw new InvalidOperationException(
                "The tenancy options differ from those the internal service provider was created "
                + "with. Contexts with different tenancy settings must not share a provider passed "
                + "to UseInternalServiceProvider.");
    }
}
