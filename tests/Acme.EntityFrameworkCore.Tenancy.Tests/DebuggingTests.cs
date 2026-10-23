using Acme.EntityFrameworkCore.Tenancy.Diagnostics;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Conventions;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Migrations.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// The debugging techniques from appendix D, each checked against the plugin: they answer
/// "is my annotation there?", "which service won?", "does my convention run?" and "what
/// does the query look like?".
/// </summary>
public class DebuggingTests
{
    [Fact]
    public void Model_debug_string_shows_plugin_annotations()
    {
        using var ctx = Contexts.Create();

        var text = ctx.DesignModel().ToDebugString(MetadataDebugStringOptions.LongDefault);

        Assert.Contains("Acme:Rls:Enabled", text);
        Assert.Contains("Acme:Tenancy:TenantProperty", text);
    }

    [Fact]
    public void Resolved_service_shows_which_replacement_won()
    {
        using var ctx = Contexts.Create();

        Assert.IsType<TenancyMigrationsSqlGenerator>(ctx.GetService<IMigrationsSqlGenerator>());
    }

    [Fact]
    public void Convention_set_shows_registered_conventions()
    {
        using var ctx = Contexts.Create();

        var conventions = ctx.GetService<IConventionSetBuilder>().CreateConventionSet();

        Assert.Contains(conventions.ModelFinalizingConventions,
            c => c is TenantIsolationConvention);
    }

    [Fact]
    public void Expression_tree_can_be_printed_before_translation()
    {
        using var ctx = Contexts.Create();
        var query = ctx.Orders.Where(o => o.TenantId == EF.Functions.CurrentTenant());

        Assert.Contains("CurrentTenant", query.Expression.Print());
        Assert.Contains("current_setting", query.ToQueryString());
    }

    [Fact]
    public void LogTo_can_be_restricted_to_plugin_events()
    {
        var lines = new List<string>();
        using var ctx = Contexts.Create(configure: o => o.LogTo(
            lines.Add, [TenancyEventId.TenantIsolationApplied], LogLevel.Debug));

        _ = ctx.Model;          // building the model raises the event once per entity type

        Assert.Contains(lines, l => l.Contains("TenantIsolationApplied") && l.Contains("Order"));
    }
}
