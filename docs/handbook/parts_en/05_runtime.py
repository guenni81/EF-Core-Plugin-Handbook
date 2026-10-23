# ============================================================ PART V
S += part(
    "Run time",
    "Intervening when saving, connecting and executing, and reporting events properly.",
    ["Chapter 15: Interceptors",
     "Chapter 16: Diagnostics and logging"])

# ------------------------------------------------------------ 15
S += h1("Interceptors")
S.append(p(
    "Interceptors are hooks into EF's run time: before and after commands, connections, "
    "transactions, SaveChanges and materialization. They can observe, change values, "
    "suppress operations or replace results."))
S += data_table(
    ["Interface", "Events", "Lifetime"],
    [
        [c("ISaveChangesInterceptor"), "SavingChanges, SavedChanges, SaveChangesFailed, "
         "ThrowingConcurrencyException", "Scoped"],
        [c("IDbConnectionInterceptor"), "Opening, closing, creating the connection", "Scoped"],
        [c("IDbCommandInterceptor"), "Creating and executing commands, data readers", "Scoped"],
        [c("IDbTransactionInterceptor"), "Begin, commit, rollback, savepoints", "Scoped"],
        [c("IIdentityResolutionInterceptor"), "Conflicts when attaching already tracked "
         "entities", "Scoped"],
        [c("IMaterializationInterceptor"), "Creating and initializing entities",
         "<b>Singleton</b>"],
        [c("IQueryExpressionInterceptor"), "LINQ expression before compilation", "<b>Singleton</b>"],
    ], [1.6, 2.4, 0.8])
S.append(p(
    "The two singleton interceptors implement " + c("ISingletonInterceptor") + ". They are "
    "used once per internal provider, not per context, and must therefore be stateless "
    "and thread-safe."))

S += h2("Two ways to register")
S += data_table(
    ["Way", "Properties", "When"],
    [
        [c("optionsBuilder.AddInterceptors(...)"),
         "Instances from the application; can use app services; differ per context",
         "Interceptors with app dependencies (tenant, user, clock)"],
        [c("services.AddScoped<IInterceptor, X>()") + " in " + c("ApplyServices"),
         "Resolved from the internal provider (“injected interceptors”); have access to "
         "EF services, not to app services",
         "Purely technical interceptors without app dependencies"],
    ], [1.6, 2, 1.4])
S.append(p(
    "EF combines both ways into one list. The sample uses the first, because both "
    "interceptors need the application's tenant (chapter 6)."))

S += h2("The SaveChanges interceptor")
S.append(p(
    "It sets the tenant of new entities and prevents data of another tenant from being "
    "written or reassigned. This complements RLS (defense in depth) and produces clearer "
    "errors than the database. Chapter 27 shows which paths bypass it:"))
S += code_block('''
public class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantProvider _tenants;

    public TenantSaveChangesInterceptor(ITenantProvider tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        _tenants = tenants;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context) ApplyTenant(context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context) ApplyTenant(context);
        return ValueTask.FromResult(result);
    }
''', label="Interceptors/TenantSaveChangesInterceptor.cs (1/2)")
S += code_block('''
    internal void ApplyTenant(DbContext context)
    {
        // Which entities are affected is stored in the runtime annotation (chapter 39)
        var changes = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified
                                   or EntityState.Deleted)
            .Select(e => (Entry: e, Name: e.Metadata.GetTenantPropertyName()))
            .Where(x => x.Name is not null)
            .Select(x => (x.Entry, Property: x.Entry.Property(x.Name!)))
            .ToList();
        if (changes.Count == 0) return;

        var current = _tenants.CurrentTenantId
            ?? throw new TenancyException("No active tenant ...");

        foreach (var (entry, property) in changes)
        {
            var currentValue = (TenantId)property.CurrentValue!;
            var originalValue = (TenantId)property.OriginalValue!;
            switch (entry.State)
            {
                case EntityState.Added when currentValue.IsEmpty:
                    property.CurrentValue = current;
                    break;
                case EntityState.Added when currentValue != current:
                    throw Mismatch(entry, currentValue, current);
                case EntityState.Modified when property.IsModified:
                    throw new TenancyException("The tenant property is immutable.");
                case EntityState.Modified or EntityState.Deleted
                    when originalValue != current:
                    throw Mismatch(entry, originalValue, current);
            }
        }
    }
}
''', label="Interceptors/TenantSaveChangesInterceptor.cs (2/2, simplified)")
S += callout("important", [
    "Always implement <b>both</b> variants, synchronous and asynchronous. Depending on " +
    c("SaveChanges") + " or " + c("SaveChangesAsync") + ", EF calls only one of them. An "
    "interceptor that only overrides the asynchronous method has no effect on synchronous "
    "calls, without anyone noticing.",
], title="Synchronous and asynchronous")
S.append(p(
    "The logic lives in an " + c("internal") + " method " + c("ApplyTenant") + ". Tests "
    "can call it directly through " + c("InternalsVisibleTo") + " without needing a "
    "database (chapter 23)."))

S += h2("The connection interceptor")
S.append(p(
    "It sets the session setting that the policies and " + c("CurrentTenant()") + " "
    "evaluate every time a connection is opened. The flow (intercept opening, sync and "
    "async, fetch the tenant) is the same for every database and therefore lives in an "
    "abstract base in the core package (chapter 41). The PostgreSQL package only supplies "
    "the command. It gets the setting name from the <i>internal</i> provider via the "
    "context from the event data (" + c("context.GetService<T>()") + "), and the tenant "
    "from the <i>app</i> container:"))
S += code_block('''
// Core package
public abstract class TenantSessionInterceptor : DbConnectionInterceptor
{
    protected TenantSessionInterceptor(ITenantProvider tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        Tenants = tenants;
    }

    protected ITenantProvider Tenants { get; }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command =
            CreateSessionCommand(connection, eventData.Context, Tenants.CurrentTenantId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // ConnectionOpened (synchronous) analogous

    protected abstract DbCommand CreateSessionCommand(
        DbConnection connection, DbContext? context, TenantId? tenant);
}

// PostgreSQL package
public class TenantConnectionInterceptor : TenantSessionInterceptor
{
    public TenantConnectionInterceptor(ITenantProvider tenants)
        : base(tenants)
    {
    }

    protected override DbCommand CreateSessionCommand(
        DbConnection connection, DbContext? context, TenantId? tenant)
    {
        var settingName = context?.GetService<ITenancySingletonOptions>()
                              .Settings.SettingName
                          ?? TenancySettings.Default.SettingName;

        var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config(@name, @value, false)";
        AddParameter(command, "name", settingName);
        // Always set, even empty: no stale value remains
        AddParameter(command, "value", tenant?.ToString() ?? string.Empty);
        return command;
    }
}
''', label="Interceptors/TenantSessionInterceptor.cs (core), Runtime/TenantConnectionInterceptor.cs")
S += callout("note", [
    "Npgsql resets the session state when a connection returns to the pool. After that, " +
    c("current_setting('app.tenant_id', true)") + " returns an empty string instead of " +
    c("NULL") + ". A " + c("''::uuid") + " would abort every query. Policy and translator "
    "therefore use " + c("nullif(..., '')") + ": without a tenant the value is " +
    c("NULL") + ", the comparison is never true and no rows are visible. That is the safe "
    "direction.",
], title="Empty string instead of NULL")
S.append(p(
    "By default EF opens and closes the connection for every query. Every opening costs an "
    "extra round trip here. With many small queries it pays off to open the connection "
    "once per request explicitly (chapter 34)."))
S += h2("Where the connection interceptor does not run")
S.append(p(
    "The interceptor runs when EF opens a connection, and only then. Two situations bypass "
    "it, and both are easy to create by accident:"))
S += data_table(
    ["Situation", "Consequence", "Remedy"],
    [
        ["An already open connection is passed in (" + c("UseNpgsql(connection)") + ", " +
         c("SetDbConnection") + ")", "EF never opens it, so the interceptor never runs. The "
         "session keeps whatever value it has: none, or that of a previous tenant",
         "Pass connections closed and let EF open them; otherwise call " +
         c("Database.ApplyTenantSession()")],
        ["The connection stays open (" + c("OpenConnection") + ", an open transaction) "
         "while the tenant changes", "The new tenant only takes effect when the connection "
         "is opened again", "One context, one tenant; if that is not possible, call " +
         c("Database.ApplyTenantSession()") + " after the change"],
    ], [1.6, 1.7, 1.5])
S += code_block('''
// Already open, with a session value left over from earlier work (tenant B)
await using var connection = new NpgsqlConnection(appConnectionString);
await connection.OpenAsync();
// ... SELECT set_config('app.tenant_id', <tenant B>, false) ...

// Meant for tenant A, but EF does not open the connection: the interceptor never runs
await using var db = CreateAppContext(connection, tenantA);
var orders = await db.Orders.ToListAsync();       // the rows of tenant B!
''', label="tests/...IntegrationTests/ConnectionLifetimeTests.cs (shortened)")
S.append(p(
    "For these cases the plugin offers the explicit counterpart to the interceptor. " +
    c("Database.ApplyTenantSession()") + " writes the current tenant into the session if "
    "the connection is open, and does nothing if it is closed, because EF will then open it "
    "and the interceptor takes over. It reuses the interceptor's own command, so it works "
    "for every provider package:"))
S += code_block('''
await using var db = CreateAppContext(connection, tenantA);   // connection already open
await db.Database.ApplyTenantSessionAsync();                  // session now: tenant A
var orders = await db.Orders.ToListAsync();                   // the rows of tenant A
''', label="Usage")
S += code_block('''
public virtual async Task<bool> ApplyToOpenConnectionAsync(
    DbContext context, CancellationToken cancellationToken = default)
{
    var connection = context.Database.GetDbConnection();
    if (connection.State != ConnectionState.Open)
        return false;                     // EF opens it later; the interceptor takes over

    await using var command =
        CreateSessionCommand(connection, context, Tenants.CurrentTenantId);
    // Inside an explicit transaction the command must be enlisted in it
    command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
    await command.ExecuteNonQueryAsync(cancellationToken);
    return true;
}
''', label="Interceptors/TenantSessionInterceptor.cs (excerpt)")
S.append(p(
    "The extension method " + c("ApplyTenantSession") + " in the core package finds the "
    "interceptor among the context's interceptors and fails with a clear message if there "
    "is none. Two details matter: the method needs the context, not just the connection, "
    "because the provider reads its settings from the internal service provider; that is "
    "why " + c("CreateSessionCommand") + " receives the " + c("DbContext") + " instead of the "
    "event data. And the command must join a running transaction: PostgreSQL is lenient "
    "here, SQL Server rejects a command that is not enlisted. The SQL Server tests check "
    "exactly this case; without the line they fail."))
S += callout("important", [
    "Isolation follows the connection, not the context. A context used for several "
    "tenants, or a connection that outlives a request, carries the tenant of the moment it "
    "was opened. " + c("ConnectionLifetimeTests") + " shows both cases against a real "
    "database, together with the two remedies: a closed connection that EF opens itself, "
    "and " + c("ApplyTenantSession()") + ". A plugin whose hooks cannot cover every path "
    "should offer such an explicit API instead of only documenting the gap.",
], title="One context, one tenant")

# ------------------------------------------------------------ 16
S += h1("Diagnostics and logging")
S.append(p(
    "A professional plugin reports events through EF's logging system. Messages then end "
    "up in the same channels users configure anyway (" + c("ILogger") + ", " + c("LogTo") +
    ", " + c("DiagnosticSource") + "), and warnings can be ignored, logged or turned into "
    "errors via " + c("ConfigureWarnings") + "."))
S += h2("Event IDs")
S += code_block('''
public static class TenancyEventId
{
    public const int TenancyBaseId = 1_150_000;     // own, documented range

    private enum Id
    {
        TenantIsolationApplied = TenancyBaseId,
        TenantIsolationOptedOut,
        PoliciesWithoutRowLevelSecurity,
    }

    private static readonly string ModelPrefix = "Acme.EntityFrameworkCore.Tenancy.Model.";

    public static readonly EventId TenantIsolationApplied =
        Make(Id.TenantIsolationApplied);
    public static readonly EventId TenantIsolationOptedOut =
        Make(Id.TenantIsolationOptedOut);
    // ...

    private static EventId Make(Id id) => new((int)id, ModelPrefix + id);
}
''', label="Diagnostics/TenancyEventId.cs (shortened)")
S.append(p(
    "EF and providers occupy low ranges. A plugin chooses its own high range and "
    "documents it. Names follow EF's scheme “Assembly.Category.Event” so that prefix "
    "filters work."))

S += h2("Event definitions")
S.append(p(
    "An " + c("EventDefinition<T>") + " bundles event ID, log level, message template and the "
    "warning behavior from the options. EF caches its definitions per internal provider; "
    "the plugin does the same with its own class registered as a singleton (chapter 5):"))
S += code_block('''
public class TenancyLoggingDefinitions
{
    private EventDefinition<string>? _tenantIsolationOptedOut;

    public virtual EventDefinition<string> LogTenantIsolationOptedOut(
        IDiagnosticsLogger logger)
        => LazyInitializer.EnsureInitialized(ref _tenantIsolationOptedOut, () =>
            Create(logger, TenancyEventId.TenantIsolationOptedOut, LogLevel.Warning,
                nameof(TenancyEventId.TenantIsolationOptedOut),
                "Entity '{entityType}' implements ITenantOwned but is excluded from "
                + "tenant isolation."));

    private static EventDefinition<string> Create(
        IDiagnosticsLogger logger, EventId eventId, LogLevel level,
        string name, string message)
        => new(
            logger.Options,                       // contains the warning behavior
            eventId,
            level,
            "TenancyEventId." + name,             // appears in warning-as-error messages
            l => LoggerMessage.Define<string>(l, eventId, message));
}
''', label="Diagnostics/TenancyLoggingDefinitions.cs (excerpt)")

S += h2("The logger extension")
S.append(p(
    "The actual log method follows the pattern of EF's " + c("CoreLoggerExtensions") +
    " exactly: classic logging first, then the event data for DiagnosticSource and " +
    c("LogTo") + ":"))
S += code_block('''
private static void LogEntityType(
    IDiagnosticsLogger<DbLoggerCategory.Model> diagnostics,
    EventDefinition<string> definition,
    IReadOnlyEntityType entityType)
{
    // 1. ILogger; throws with ConfigureWarnings(w => w.Throw(...))
    if (diagnostics.ShouldLog(definition))
        definition.Log(diagnostics, entityType.DisplayName());

    // 2. DiagnosticSource and LogTo (simple logging)
    if (diagnostics.NeedsEventData(definition, out var diagnosticSource, out var simpleLog))
    {
        var eventData = new EntityTypeEventData(definition, GenerateMessage, entityType);
        diagnostics.DispatchEventData(definition, eventData, diagnosticSource, simpleLog);
    }
}
''', label="Diagnostics/TenancyLoggerExtensions.cs (excerpt)")
S.append(p("Users can then turn the warning into an error:"))
S += code_block('''
options.UseNpgsql(cs)
       .UseAcmeTenancy()
       .ConfigureWarnings(w => w.Throw(TenancyEventId.TenantIsolationOptedOut));

// -> InvalidOperationException while building the model:
//    "An error was generated for warning '...TenantIsolationOptedOut': Entity
//     'AuditEntry' ... This exception can be suppressed or logged by passing event ID
//     'TenancyEventId.TenantIsolationOptedOut' to the 'ConfigureWarnings' method ..."
''', label="Usage")
S += callout("tip", [
    "Security-relevant deviations belong in the log as a <b>warning</b>, not as a debug "
    "message. Teams with strict requirements escalate them to errors via " +
    c("ConfigureWarnings") + "; others at least see them at startup. The plugin does not "
    "have to make this decision for everyone.",
])
