using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Update;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests.UpdatePipeline;

/// <summary>Entity of the example: UpdatedAt from the database clock, xmin as concurrency token.</summary>
public class Document
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public uint Version { get; set; }
}

/// <summary>Context of the example; the tenancy plugin is active as well.</summary>
public sealed class DocumentContext : DbContext
{
    public DocumentContext(DbContextOptions<DocumentContext> options)
        : base(options)
    {
    }

    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Document>(b =>
        {
            b.Property(d => d.UpdatedAt).HasDatabaseClockOnUpdate();
            b.Property(d => d.Version).IsRowVersion();          // Npgsql: the system column xmin
        });

    /// <summary>Options as a plugin would register them: the replaced singleton service.</summary>
    public static DocumentContext Create(string connectionString, List<string>? sql = null)
    {
        var builder = new DbContextOptionsBuilder<DocumentContext>()
            .UseNpgsql(connectionString)
            .UseAcmeTenancy()
            .ReplaceService<IUpdateSqlGenerator, DatabaseClockUpdateSqlGenerator>()
            .EnableServiceProviderCaching(false);
        if (sql is not null)
            builder.AddInterceptors(new CommandRecorder(sql));
        return new DocumentContext(builder.Options);
    }

    private sealed class CommandRecorder(List<string> sql) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            sql.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}

/// <summary>
/// The update pipeline example (chapter "The update pipeline") against PostgreSQL: the SQL that
/// SaveChanges sends, the value read back, and optimistic concurrency.
/// </summary>
public class UpdatePipelineTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public UpdatePipelineTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private async Task<string> CreateDatabaseAsync()
    {
        var (owner, _) = await TestDatabase.CreateAsync(_fixture);
        await using var db = DocumentContext.Create(owner);
        await db.Database.EnsureCreatedAsync();
        return owner;
    }

    private static async Task<DateTime> DatabaseUpdatedAtAsync(string connectionString, int id)
    {
        await using var db = DocumentContext.Create(connectionString);
        return await db.Documents.Where(d => d.Id == id).Select(d => d.UpdatedAt).SingleAsync();
    }

    [Fact]
    public async Task Update_sets_the_column_to_now_and_reads_it_back()
    {
        var connectionString = await CreateDatabaseAsync();
        var sql = new List<string>();
        await using var db = DocumentContext.Create(connectionString, sql);

        var document = new Document { Title = "draft" };
        db.Add(document);
        await db.SaveChangesAsync();
        var inserted = document.UpdatedAt;
        Assert.NotEqual(default, inserted);                    // column default, via RETURNING

        await Task.Delay(10);
        document.Title = "final";
        await db.SaveChangesAsync();

        var update = Assert.Single(sql, s => s.StartsWith("UPDATE", StringComparison.Ordinal));
        Assert.Contains("SET \"Title\" = @p0, \"UpdatedAt\" = now()", update, StringComparison.Ordinal);
        Assert.Contains("WHERE \"Id\" = @p1 AND xmin = @p2", update, StringComparison.Ordinal);
        Assert.Contains("RETURNING \"UpdatedAt\", xmin", update, StringComparison.Ordinal);
        Assert.DoesNotContain(sql, s => s.StartsWith("INSERT", StringComparison.Ordinal)
                                        && s.Contains("\"UpdatedAt\")", StringComparison.Ordinal));

        // The entity holds the database's value, not a value of the application
        Assert.True(document.UpdatedAt > inserted);
        Assert.Equal(await DatabaseUpdatedAtAsync(connectionString, document.Id), document.UpdatedAt);
    }

    [Fact]
    public async Task Optimistic_concurrency_still_detects_a_conflicting_update()
    {
        var connectionString = await CreateDatabaseAsync();
        int id;
        await using (var setup = DocumentContext.Create(connectionString))
        {
            var document = new Document { Title = "v1" };
            setup.Add(document);
            await setup.SaveChangesAsync();
            id = document.Id;
        }

        await using var first = DocumentContext.Create(connectionString);
        await using var second = DocumentContext.Create(connectionString);
        var a = await first.Documents.SingleAsync(d => d.Id == id);
        var b = await second.Documents.SingleAsync(d => d.Id == id);

        a.Title = "first";
        await first.SaveChangesAsync();
        var version = a.Version;

        b.Title = "second";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        // The second writer changed nothing, also not the database clock column
        await using var check = DocumentContext.Create(connectionString);
        var stored = await check.Documents.SingleAsync(d => d.Id == id);
        Assert.Equal("first", stored.Title);
        Assert.Equal(version, stored.Version);
        Assert.Equal(a.UpdatedAt, stored.UpdatedAt);
    }
}
