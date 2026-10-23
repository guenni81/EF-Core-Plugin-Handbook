using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// The options extension: storage and validation of settings, the Npgsql requirement and
/// correct service provider caching.
/// </summary>
public class OptionsTests
{
    [Fact]
    public void Settings_are_stored_in_the_extension()
    {
        using var ctx = Contexts.Create(t => t.UseSettingName("app.mandant").ApplyQueryFilter());
        var settings = ctx.GetService<IDbContextOptions>()
            .FindExtension<TenancyOptionsExtension>()!.Settings;

        Assert.Equal("app.mandant", settings.SettingName);
        Assert.True(settings.ApplyQueryFilter);
    }

    [Theory]
    [InlineData("tenant_id")]               // no prefix
    [InlineData("App.Tenant")]              // upper-case letters
    [InlineData("app.x'); DROP --")]        // injection attempt
    public void Invalid_setting_name_is_rejected(string setting)
        => Assert.Throws<ArgumentException>(() => Contexts.Create(t => t.UseSettingName(setting)));

    [Fact]
    public void Requires_npgsql()
    {
        var options = new DbContextOptionsBuilder<TestContext>()
            .UseAcmeTenancy()
            .Options;

        // EF validates the options extensions as soon as the internal service provider is
        // built, which already happens in the DbContext constructor.
        Assert.Throws<InvalidOperationException>(() => new TestContext(options, null));
    }

    [Fact]
    public void Different_settings_produce_different_models_with_caching_enabled()
    {
        // Catches the classic mistake: model-relevant options missing from the extension's hash.
        // With caching enabled, both contexts would otherwise share one internal service
        // provider and therefore one cached model.
        using var a = Contexts.Create(t => t.UseSettingName("app.tenant_id"), cacheServiceProvider: true);
        using var b = Contexts.Create(t => t.UseSettingName("app.mandant_id"), cacheServiceProvider: true);

        Assert.NotSame(a.DesignModel(), b.DesignModel());
        Assert.Contains("app.tenant_id", Contexts.MigrationSql(null, a).First(s => s.Contains("POLICY")));
        Assert.Contains("app.mandant_id", Contexts.MigrationSql(null, b).First(s => s.Contains("POLICY")));
    }
}
