using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;

/// <summary>
/// Options for singleton services (e.g. type mapping plugins, interceptors) that cannot
/// resolve <see cref="IDbContextOptions"/> per context.
/// </summary>
public interface ITenancySingletonOptions : ISingletonOptions
{
    /// <summary>The settings the internal service provider was built with.</summary>
    TenancySettings Settings { get; }
}

/// <inheritdoc />
public class TenancySingletonOptions : ITenancySingletonOptions
{
    /// <inheritdoc />
    public TenancySettings Settings { get; private set; } = TenancySettings.Default;

    /// <summary>Called once per internal service provider.</summary>
    /// <param name="options">Options of the first context that built the provider.</param>
    public virtual void Initialize(IDbContextOptions options)
        => Settings = options.FindExtension<TenancyOptionsExtension>()?.Settings
                      ?? TenancySettings.Default;

    /// <summary>For every context: options must not differ from the provider's state.</summary>
    /// <param name="options">Options of the current context.</param>
    /// <exception cref="InvalidOperationException">The tenancy settings differ.</exception>
    public virtual void Validate(IDbContextOptions options)
    {
        var settings = options.FindExtension<TenancyOptionsExtension>()?.Settings
                       ?? TenancySettings.Default;

        if (settings != Settings)
            throw new InvalidOperationException(
                "The tenancy options were changed after the internal service provider was "
                + "created. Check GetServiceProviderHashCode of the options extension.");
    }
}
