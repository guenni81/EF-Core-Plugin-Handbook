using System.Diagnostics;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests;

/// <summary>
/// EnrichOpenTelemetry() against PostgreSQL (chapter "Diagnostics and logging"): the current
/// activity carries the tenant of the context that runs the command.
/// </summary>
public sealed class ActivityEnrichmentTests : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly PostgresFixture _fixture;
    private readonly ActivitySource _source = new("Acme.Tests.Tracing");
    private readonly ActivityListener _listener;

    public ActivityEnrichmentTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
        // Without a listener no activity is created at all; this one records everything
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s == _source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
    }

    private async Task<object?> TagAfterQueryAsync(TenantId? tenant, bool enrich)
    {
        await using var db = _fixture.CreateAppContext(
            new MutableTenantProvider { CurrentTenantId = tenant },
            t => t.EnrichOpenTelemetry(enrich));

        using var activity = _source.StartActivity("request");
        await db.Orders.CountAsync();
        return activity!.GetTagItem(TenantActivityEnrichmentInterceptor.TagName);
    }

    [Fact]
    public async Task Enabled_tags_the_current_activity_with_the_tenant()
    {
        var tenant = TenantId.New();

        Assert.Equal(tenant.Value.ToString("D"), await TagAfterQueryAsync(tenant, enrich: true));
    }

    [Fact]
    public async Task Disabled_adds_no_tag()
    {
        Assert.Null(await TagAfterQueryAsync(TenantId.New(), enrich: false));
    }

    [Fact]
    public async Task Without_a_tenant_no_tag_is_added()
    {
        Assert.Null(await TagAfterQueryAsync(tenant: null, enrich: true));
    }

    [Fact]
    public async Task Without_an_activity_the_query_runs_unchanged()
    {
        await using var db = _fixture.CreateAppContext(
            new MutableTenantProvider { CurrentTenantId = TenantId.New() },
            t => t.EnrichOpenTelemetry());

        Assert.Null(Activity.Current);
        Assert.Equal(0, await db.Orders.CountAsync());
    }
}
