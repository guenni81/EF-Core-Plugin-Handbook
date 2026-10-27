using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// The failure cases of chapter "State, lifetimes and thread safety" without a database: each
/// wrong variant next to the correct one. The wrong classes exist only in this file.
/// </summary>
public class StateAndLifetimeTests
{
    // ------------------------------------------------ 1. Current tenant in a singleton

    /// <summary>WRONG: a singleton tenant provider that remembers the first tenant.</summary>
    private sealed class RememberingTenantProvider : ITenantProvider
    {
        private TenantId? _tenant;

        public TenantId? CurrentTenantId => _tenant ??= new AmbientTenantProvider().CurrentTenantId;
    }

    private static TenantId StampNewOrder(ITenantProvider tenants)
    {
        using var ctx = Contexts.Create();
        var order = new Order();
        ctx.Orders.Add(order);
        new TenantSaveChangesInterceptor(tenants).ApplyTenant(ctx);
        return order.TenantId;
    }

    [Fact]
    public void Singleton_that_remembers_the_tenant_stamps_the_first_tenant_on_later_requests()
    {
        var remembering = new RememberingTenantProvider();    // one instance, as with pooling
        TenantId a = TenantId.New(), b = TenantId.New();

        using (AmbientTenantProvider.Use(a))
            Assert.Equal(a, StampNewOrder(remembering));

        using (AmbientTenantProvider.Use(b))
            Assert.Equal(a, StampNewOrder(remembering));      // request B writes tenant A
    }

    [Fact]
    public void Singleton_that_reads_the_tenant_on_every_access_stamps_the_current_tenant()
    {
        var ambient = new AmbientTenantProvider();
        TenantId a = TenantId.New(), b = TenantId.New();

        using (AmbientTenantProvider.Use(a))
            Assert.Equal(a, StampNewOrder(ambient));

        using (AmbientTenantProvider.Use(b))
            Assert.Equal(b, StampNewOrder(ambient));
    }

    // ------------------------------------------------ 2. Current tenant in the hash

    /// <summary>WRONG: an options extension that carries the tenant into the provider key.</summary>
    private sealed class TenantInHashExtension : IDbContextOptionsExtension
    {
        public TenantInHashExtension(TenantId tenant) => Tenant = tenant;

        public TenantId Tenant { get; }

        public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

        public void ApplyServices(IServiceCollection services)
        {
        }

        public void Validate(IDbContextOptions options)
        {
        }

        private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
        {
            public ExtensionInfo(TenantInHashExtension extension)
                : base(extension)
            {
            }

            private TenantId Tenant => ((TenantInHashExtension)Extension).Tenant;

            public override bool IsDatabaseProvider => false;

            public override string LogFragment => "";

            public override int GetServiceProviderHashCode() => Tenant.GetHashCode();

            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
                => other is ExtensionInfo o && o.Tenant == Tenant;

            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            {
            }
        }
    }

    [Fact]
    public void Tenant_in_the_service_provider_key_needs_a_provider_per_tenant()
    {
        var a = new TenantInHashExtension(TenantId.New()).Info;
        var b = new TenantInHashExtension(TenantId.New()).Info;

        // Different key: every tenant gets its own internal provider and model
        Assert.False(a.ShouldUseSameServiceProvider(b));
    }

    [Fact]
    public void Tenant_through_interceptors_keeps_one_provider_for_all_tenants()
    {
        static DbContextOptions Options(TenantId tenant)
            => new DbContextOptionsBuilder()
                .UseNpgsql("Host=localhost;Database=unused")
                .UseAcmeTenancy()
                .AddInterceptors(new TenantSaveChangesInterceptor(
                    new FixedTenant(tenant)))
                .Options;

        var a = Options(TenantId.New());
        var b = Options(TenantId.New());

        // Every extension that takes part in the key considers both option sets equal
        foreach (var extension in a.Extensions)
        {
            var other = b.Extensions.Single(e => e.GetType() == extension.GetType());
            Assert.Equal(extension.Info.GetServiceProviderHashCode(),
                other.Info.GetServiceProviderHashCode());
            Assert.True(extension.Info.ShouldUseSameServiceProvider(other.Info));
        }
    }

    private sealed class FixedTenant : ITenantProvider
    {
        public FixedTenant(TenantId? id) => CurrentTenantId = id;

        public TenantId? CurrentTenantId { get; }
    }

    // ------------------------------------------------ 3. Scoped service held by a singleton

    /// <summary>Plugin service that needs the current context.</summary>
    private sealed class ContextHolder
    {
        public ContextHolder(ICurrentDbContext current) => Current = current;

        public ICurrentDbContext? Current { get; }
    }

    /// <summary>Registers <see cref="ContextHolder"/> with the given lifetime.</summary>
    private sealed class HolderExtension : IDbContextOptionsExtension
    {
        private readonly ServiceLifetime _lifetime;

        public HolderExtension(ServiceLifetime lifetime) => _lifetime = lifetime;

        public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

        public void ApplyServices(IServiceCollection services)
            => new EntityFrameworkServicesBuilder(services).TryAddProviderSpecificServices(m =>
            {
                if (_lifetime == ServiceLifetime.Singleton)
                    m.TryAddSingleton<ContextHolder, ContextHolder>();     // WRONG
                else
                    m.TryAddScoped<ContextHolder, ContextHolder>();
            });

        public void Validate(IDbContextOptions options)
        {
        }

        private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
        {
            public ExtensionInfo(HolderExtension extension)
                : base(extension)
            {
            }

            public override bool IsDatabaseProvider => false;

            public override string LogFragment => "";

            public override int GetServiceProviderHashCode()
                => ((HolderExtension)Extension)._lifetime.GetHashCode();

            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
                => other is ExtensionInfo o
                   && ((HolderExtension)o.Extension)._lifetime == ((HolderExtension)Extension)._lifetime;

            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            {
            }
        }
    }

    private static TestContext ContextWithHolder(ServiceLifetime lifetime)
        => Contexts.Create(configure: o => ((IDbContextOptionsBuilderInfrastructure)o)
            .AddOrUpdateExtension(new HolderExtension(lifetime)));

    [Fact]
    public void Singleton_holding_a_context_bound_service_gets_no_context()
    {
        using var ctx = ContextWithHolder(ServiceLifetime.Singleton);

        // No error at registration: EF does not validate scopes in its internal provider. The
        // singleton is created in the root scope, where no context exists.
        Assert.Null(ctx.GetService<ContextHolder>().Current);
    }

    [Fact]
    public void Scoped_service_gets_its_own_context()
    {
        using var ctx = ContextWithHolder(ServiceLifetime.Scoped);

        Assert.Same(ctx, ctx.GetService<ContextHolder>().Current!.Context);
    }

    // ------------------------------------------------ 4. Options extension changed later

    /// <summary>WRONG: a setting that can still be changed after registration.</summary>
    private sealed class MutableExtension : IDbContextOptionsExtension
    {
        private DbContextOptionsExtensionInfo? _info;

        public string Setting { get; set; } = "A";

        public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

        public void ApplyServices(IServiceCollection services)
            => new EntityFrameworkServicesBuilder(services)
                .TryAdd<ISingletonOptions, MutableSingletonOptions>(
                    p => p.GetRequiredService<MutableSingletonOptions>())
                .TryAddProviderSpecificServices(m =>
                    m.TryAddSingleton<MutableSingletonOptions, MutableSingletonOptions>());

        public void Validate(IDbContextOptions options)
        {
        }

        private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
        {
            private int? _hash;

            public ExtensionInfo(MutableExtension extension)
                : base(extension)
            {
            }

            public override bool IsDatabaseProvider => false;

            public override string LogFragment => "";

            // Computed once, like EF's own extensions do
            public override int GetServiceProviderHashCode()
                => _hash ??= ((MutableExtension)Extension).Setting.GetHashCode();

            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
                => other is ExtensionInfo;

            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            {
            }
        }
    }

    /// <summary>Reads the setting once per internal provider and does not validate.</summary>
    private sealed class MutableSingletonOptions : ISingletonOptions
    {
        public string Setting { get; private set; } = "";

        public void Initialize(IDbContextOptions options)
            => Setting = options.FindExtension<MutableExtension>()!.Setting;

        public void Validate(IDbContextOptions options)
        {
        }
    }

    [Fact]
    public void Extension_changed_after_registration_is_not_seen_by_the_provider()
    {
        var extension = new MutableExtension();
        var builder = new DbContextOptionsBuilder<TestContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .UseAcmeTenancy();
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);

        // One internal provider for this configuration (not the global cache of the tests)
        var services = new ServiceCollection().AddEntityFrameworkNpgsql();
        new TenancyOptionsExtension().ApplyServices(services);
        extension.ApplyServices(services);
        using var internalProvider = services.BuildServiceProvider();
        builder.UseInternalServiceProvider(internalProvider);

        using (var first = new TestContext(builder.Options, null))
            Assert.Equal("A", first.GetService<MutableSingletonOptions>().Setting);

        extension.Setting = "B";                                    // after registration

        using var second = new TestContext(builder.Options, null);
        Assert.Equal("A", second.GetService<MutableSingletonOptions>().Setting);   // stale
    }

    [Fact]
    public void Shared_provider_rejects_contexts_with_different_tenancy_settings()
    {
        var services = new ServiceCollection().AddEntityFrameworkNpgsql();
        new TenancyOptionsExtension().ApplyServices(services);
        using var internalProvider = services.BuildServiceProvider();

        TestContext Create(string settingName)
            => new(new DbContextOptionsBuilder<TestContext>()
                .UseNpgsql("Host=localhost;Database=unused")
                .UseAcmeTenancy(t => t.UseSettingName(settingName))
                .UseInternalServiceProvider(internalProvider)
                .Options, null);

        using (var first = Create("app.tenant_a"))
            _ = first.Model;

        // TenancySingletonOptions.Validate: the shared provider was initialized differently
        using var second = Create("app.tenant_b");
        var ex = Assert.Throws<InvalidOperationException>(() => second.Model);
        Assert.Contains("UseInternalServiceProvider", ex.Message);
    }
}
