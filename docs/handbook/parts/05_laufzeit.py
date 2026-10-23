# ============================================================ TEIL V
S += part(
    "Laufzeit",
    "Eingreifen beim Speichern, Verbinden und Ausführen, und Ereignisse sauber melden.",
    ["Kapitel 15: Interceptors",
     "Kapitel 16: Diagnose und Logging"])

# ------------------------------------------------------------ 15
S += h1("Interceptors")
S.append(p(
    "Interceptors sind Haken in EFs Laufzeit: vor und nach Befehlen, Verbindungen, "
    "Transaktionen, SaveChanges und der Materialisierung. Sie können beobachten, Werte "
    "ändern, Operationen unterdrücken oder Ergebnisse ersetzen."))
S += data_table(
    ["Interface", "Ereignisse", "Lebensdauer"],
    [
        [c("ISaveChangesInterceptor"), "SavingChanges, SavedChanges, SaveChangesFailed, "
         "ThrowingConcurrencyException", "Scoped"],
        [c("IDbConnectionInterceptor"), "Öffnen, Schließen, Erzeugen der Verbindung", "Scoped"],
        [c("IDbCommandInterceptor"), "Erzeugen und Ausführen von Befehlen, DataReader", "Scoped"],
        [c("IDbTransactionInterceptor"), "Beginnen, Commit, Rollback, Savepoints", "Scoped"],
        [c("IIdentityResolutionInterceptor"), "Konflikte beim Anhängen bereits getrackter "
         "Entitäten", "Scoped"],
        [c("IMaterializationInterceptor"), "Erzeugen und Initialisieren von Entitäten",
         "<b>Singleton</b>"],
        [c("IQueryExpressionInterceptor"), "LINQ-Ausdruck vor der Kompilierung", "<b>Singleton</b>"],
    ], [1.6, 2.4, 0.8])
S.append(p(
    "Die beiden Singleton-Interceptors implementieren " + c("ISingletonInterceptor") + ". Sie "
    "werden einmal pro internem Provider verwendet, nicht pro Context, und müssen deshalb "
    "zustandslos und thread-sicher sein."))

S += h2("Zwei Wege der Registrierung")
S += data_table(
    ["Weg", "Eigenschaften", "Wann"],
    [
        [c("optionsBuilder.AddInterceptors(...)"),
         "Instanzen aus der Anwendung; können App-Dienste nutzen; pro Context verschieden",
         "Interceptors mit App-Abhängigkeiten (Mandant, Benutzer, Uhr)"],
        [c("services.AddScoped<IInterceptor, X>()") + " in " + c("ApplyServices"),
         "Im internen Provider aufgelöst („injected interceptors“); haben Zugriff auf "
         "EF-Dienste, nicht auf App-Dienste",
         "Rein technische Interceptors ohne App-Abhängigkeit"],
    ], [1.6, 2, 1.4])
S.append(p(
    "Beide Wege kombiniert EF zu einer Liste. Das Beispiel nutzt den ersten, weil beide "
    "Interceptors den Mandanten der Anwendung brauchen (Kapitel 6)."))

S += h2("Der SaveChanges-Interceptor")
S.append(p(
    "Er setzt die " + c("TenantId") + " neuer Entitäten und verhindert, dass Daten eines "
    "anderen Mandanten geschrieben oder umgehängt werden. Das ergänzt RLS (Defense in Depth) "
    "und liefert verständlichere Fehler als die Datenbank. Kapitel 27 zeigt, welche Wege "
    "an ihm vorbeiführen:"))
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
        // Welche Entitäten betroffen sind, steht in der Laufzeit-Annotation (Kapitel 39)
        var changes = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified
                                   or EntityState.Deleted)
            .Select(e => (Entry: e, Name: e.Metadata.GetTenantPropertyName()))
            .Where(x => x.Name is not null)
            .Select(x => (x.Entry, Property: x.Entry.Property(x.Name!)))
            .ToList();
        if (changes.Count == 0) return;

        var current = _tenants.CurrentTenantId
            ?? throw new TenancyException("Kein aktiver Mandant ...");

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
                    throw new TenancyException("Mandanten-Property ist unveränderlich.");
                case EntityState.Modified or EntityState.Deleted
                    when originalValue != current:
                    throw Mismatch(entry, originalValue, current);
            }
        }
    }
}
''', label="Interceptors/TenantSaveChangesInterceptor.cs (2/2, vereinfacht)")
S += callout("wichtig", [
    "Immer <b>beide</b> Varianten implementieren, synchron und asynchron. EF ruft je nach "
    + c("SaveChanges") + " oder " + c("SaveChangesAsync") + " nur eine davon auf. Ein "
    "Interceptor, der nur die asynchrone Methode überschreibt, ist bei synchronen Aufrufen "
    "wirkungslos, ohne dass es jemand bemerkt.",
], title="Synchron und asynchron")
S.append(p(
    "Die Logik liegt in einer " + c("internal") + "-Methode " + c("ApplyTenant") + ". Tests "
    "können sie über " + c("InternalsVisibleTo") + " direkt aufrufen, ohne eine Datenbank zu "
    "brauchen (Kapitel 23)."))

S += h2("Der Connection-Interceptor")
S.append(p(
    "Er setzt bei jedem Öffnen der Verbindung das Session-Setting, das die Policies und " +
    c("CurrentTenant()") + " auswerten. Der Ablauf (Öffnen abfangen, sync und async, "
    "Mandant holen) ist für jede Datenbank gleich und liegt deshalb in einer abstrakten "
    "Basis im Kernpaket (Kapitel 41). Das PostgreSQL-Paket liefert nur den Befehl. Den "
    "Setting-Namen holt es über den Context aus den Event-Daten (" +
    c("context.GetService<T>()") + ") aus dem <i>internen</i> Provider, den Mandanten aus "
    "dem <i>App</i>-Container:"))
S += code_block('''
// Kernpaket
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

    // ConnectionOpened (synchron) analog

    protected abstract DbCommand CreateSessionCommand(
        DbConnection connection, DbContext? context, TenantId? tenant);
}

// PostgreSQL-Paket
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
        // Immer setzen, auch leer: kein alter Wert bleibt stehen
        AddParameter(command, "value", tenant?.ToString() ?? string.Empty);
        return command;
    }
}
''', label="Interceptors/TenantSessionInterceptor.cs (Kern), Runtime/TenantConnectionInterceptor.cs")
S += callout("hinweis", [
    "Npgsql setzt den Session-Zustand zurück, wenn eine Verbindung in den Pool zurückkehrt. "
    "Danach liefert " + c("current_setting('app.tenant_id', true)") + " einen leeren String "
    "statt " + c("NULL") + ". Ein " + c("''::uuid") + " würde jede Abfrage abbrechen. "
    "Policy und Translator verwenden deshalb " + c("nullif(..., '')") + ": Ohne Mandant ist "
    "der Wert " + c("NULL") + ", der Vergleich nie wahr, und es sind keine Zeilen sichtbar. "
    "Das ist die sichere Richtung.",
], title="Leerer String statt NULL")
S.append(p(
    "EF öffnet und schließt die Verbindung standardmäßig für jede Abfrage. Jedes Öffnen "
    "kostet hier einen zusätzlichen Roundtrip. Bei vielen kleinen Abfragen lohnt es sich, "
    "die Verbindung pro Anfrage einmal explizit zu öffnen (Kapitel 34)."))
S += h2("Wo der Connection-Interceptor nicht läuft")
S.append(p(
    "Der Interceptor läuft, wenn EF eine Verbindung öffnet, und nur dann. Zwei Situationen "
    "umgehen ihn, und beide entstehen leicht aus Versehen:"))
S += data_table(
    ["Situation", "Folge", "Abhilfe"],
    [
        ["Eine bereits geöffnete Verbindung wird übergeben (" + c("UseNpgsql(connection)") +
         ", " + c("SetDbConnection") + ")", "EF öffnet sie nie, also läuft der Interceptor "
         "nie. Die Session behält ihren bisherigen Wert: keinen oder den eines früheren "
         "Mandanten", "Verbindungen geschlossen übergeben und EF öffnen lassen; sonst " +
         c("Database.ApplyTenantSession()") + " aufrufen"],
        ["Die Verbindung bleibt offen (" + c("OpenConnection") + ", eine offene Transaktion), "
         "während der Mandant wechselt", "Der neue Mandant wirkt erst, wenn die Verbindung "
         "erneut geöffnet wird", "Ein Context, ein Mandant; ist das nicht möglich, nach "
         "dem Wechsel " + c("Database.ApplyTenantSession()") + " aufrufen"],
    ], [1.6, 1.7, 1.5])
S += code_block('''
// Bereits offen, mit einem Session-Wert aus früherer Arbeit (Mandant B)
await using var connection = new NpgsqlConnection(appConnectionString);
await connection.OpenAsync();
// ... SELECT set_config('app.tenant_id', <tenant B>, false) ...

// Gedacht für Mandant A, aber EF öffnet die Verbindung nicht: kein Interceptor
await using var db = CreateAppContext(connection, tenantA);
var orders = await db.Orders.ToListAsync();       // die Zeilen von Mandant B!
''', label="tests/...IntegrationTests/ConnectionLifetimeTests.cs (gekürzt)")
S.append(p(
    "Für diese Fälle bietet das Plugin das explizite Gegenstück zum Interceptor. " +
    c("Database.ApplyTenantSession()") + " schreibt den aktuellen Mandanten in die Session, "
    "wenn die Verbindung offen ist, und tut nichts, wenn sie geschlossen ist, denn dann "
    "öffnet EF sie und der Interceptor übernimmt. Die Methode verwendet den Befehl des "
    "Interceptors selbst und funktioniert deshalb mit jedem Provider-Paket:"))
S += code_block('''
await using var db = CreateAppContext(connection, tenantA);   // Verbindung bereits offen
await db.Database.ApplyTenantSessionAsync();                  // Session jetzt: Mandant A
var orders = await db.Orders.ToListAsync();                   // die Zeilen von Mandant A
''', label="Verwendung")
S += code_block('''
public virtual async Task<bool> ApplyToOpenConnectionAsync(
    DbContext context, CancellationToken cancellationToken = default)
{
    var connection = context.Database.GetDbConnection();
    if (connection.State != ConnectionState.Open)
        return false;                     // EF öffnet sie später; der Interceptor übernimmt

    await using var command =
        CreateSessionCommand(connection, context, Tenants.CurrentTenantId);
    // In einer expliziten Transaktion muss der Befehl ihr zugeordnet sein
    command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
    await command.ExecuteNonQueryAsync(cancellationToken);
    return true;
}
''', label="Interceptors/TenantSessionInterceptor.cs (Auszug)")
S.append(p(
    "Die Erweiterungsmethode " + c("ApplyTenantSession") + " im Kernpaket sucht den "
    "Interceptor unter den Interceptoren des Contexts und bricht mit einer klaren Meldung "
    "ab, wenn keiner registriert ist. Zwei Details sind wichtig: Die Methode braucht den "
    "Context, nicht nur die Verbindung, weil der Provider seine Einstellungen aus dem "
    "internen Service Provider liest; deshalb erhält " + c("CreateSessionCommand") + " den " +
    c("DbContext") + " statt der Event-Daten. Und der Befehl muss sich einer laufenden "
    "Transaktion anschließen: PostgreSQL ist hier nachsichtig, SQL Server lehnt einen "
    "Befehl ab, der ihr nicht zugeordnet ist. Die SQL-Server-Tests prüfen genau diesen "
    "Fall; ohne die Zeile schlagen sie fehl."))
S += callout("wichtig", [
    "Die Isolation folgt der Verbindung, nicht dem Context. Ein Context, der für mehrere "
    "Mandanten verwendet wird, oder eine Verbindung, die eine Anfrage überdauert, trägt den "
    "Mandanten des Moments, in dem sie geöffnet wurde. " + c("ConnectionLifetimeTests") +
    " zeigt beide Fälle gegen eine echte Datenbank, zusammen mit den beiden Abhilfen: "
    "einer geschlossenen Verbindung, die EF selbst öffnet, und " +
    c("ApplyTenantSession()") + ". Ein Plugin, dessen Hooks nicht jeden Weg abdecken, "
    "sollte eine solche explizite API anbieten, statt die Lücke nur zu dokumentieren.",
], title="Ein Context, ein Mandant")

# ------------------------------------------------------------ 16
S += h1("Diagnose und Logging")
S.append(p(
    "Ein professionelles Plugin meldet Ereignisse über EFs Logging-System. Dann landen die "
    "Meldungen in denselben Kanälen, die Nutzer ohnehin konfigurieren (" + c("ILogger") +
    ", " + c("LogTo") + ", " + c("DiagnosticSource") + "), und Warnungen lassen sich per " +
    c("ConfigureWarnings") + " ignorieren, protokollieren oder zu Fehlern machen."))
S += h2("Event-IDs")
S += code_block('''
public static class TenancyEventId
{
    public const int TenancyBaseId = 1_150_000;     // eigener, dokumentierter Bereich

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
''', label="Diagnostics/TenancyEventId.cs (gekürzt)")
S.append(p(
    "EF und Provider belegen niedrige Bereiche. Ein Plugin wählt einen eigenen, hohen "
    "Bereich und dokumentiert ihn. Die Namen folgen EFs Schema „Assembly.Kategorie.Ereignis“, "
    "damit Filter nach Präfix funktionieren."))

S += h2("Event-Definitionen")
S.append(p(
    "Eine " + c("EventDefinition<T>") + " bündelt Event-ID, Log-Level, Meldungsvorlage und "
    "das Warnverhalten aus den Options. EF cached seine Definitionen pro internem Provider; "
    "das Plugin macht es genauso mit einer eigenen Klasse, die als Singleton registriert "
    "ist (Kapitel 5):"))
S += code_block('''
public class TenancyLoggingDefinitions
{
    private EventDefinition<string>? _tenantIsolationOptedOut;

    public virtual EventDefinition<string> LogTenantIsolationOptedOut(
        IDiagnosticsLogger logger)
        => LazyInitializer.EnsureInitialized(ref _tenantIsolationOptedOut, () =>
            Create(logger, TenancyEventId.TenantIsolationOptedOut, LogLevel.Warning,
                nameof(TenancyEventId.TenantIsolationOptedOut),
                "Entität '{entityType}' implementiert ITenantOwned, ist aber von der "
                + "Mandantenisolation ausgenommen."));

    private static EventDefinition<string> Create(
        IDiagnosticsLogger logger, EventId eventId, LogLevel level,
        string name, string message)
        => new(
            logger.Options,                       // enthält das Warnverhalten
            eventId,
            level,
            "TenancyEventId." + name,             // steht in Warning-as-Error-Meldungen
            l => LoggerMessage.Define<string>(l, eventId, message));
}
''', label="Diagnostics/TenancyLoggingDefinitions.cs (Auszug)")

S += h2("Die Logger-Extension")
S.append(p(
    "Die eigentliche Log-Methode folgt exakt dem Muster aus EFs " +
    c("CoreLoggerExtensions") + ": erst klassisches Logging, dann die Ereignisdaten für "
    "DiagnosticSource und " + c("LogTo") + ":"))
S += code_block('''
private static void LogEntityType(
    IDiagnosticsLogger<DbLoggerCategory.Model> diagnostics,
    EventDefinition<string> definition,
    IReadOnlyEntityType entityType)
{
    // 1. ILogger; wirft bei ConfigureWarnings(w => w.Throw(...))
    if (diagnostics.ShouldLog(definition))
        definition.Log(diagnostics, entityType.DisplayName());

    // 2. DiagnosticSource und LogTo (simple logging)
    if (diagnostics.NeedsEventData(definition, out var diagnosticSource, out var simpleLog))
    {
        var eventData = new EntityTypeEventData(definition, GenerateMessage, entityType);
        diagnostics.DispatchEventData(definition, eventData, diagnosticSource, simpleLog);
    }
}
''', label="Diagnostics/TenancyLoggerExtensions.cs (Auszug)")
S.append(p("Nutzer können die Warnung damit in einen Fehler verwandeln:"))
S += code_block('''
options.UseNpgsql(cs)
       .UseAcmeTenancy()
       .ConfigureWarnings(w => w.Throw(TenancyEventId.TenantIsolationOptedOut));

// -> InvalidOperationException beim Modellbau:
//    "An error was generated for warning '...TenantIsolationOptedOut': Entität
//     'AuditEntry' ... This exception can be suppressed or logged by passing event ID
//     'TenancyEventId.TenantIsolationOptedOut' to the 'ConfigureWarnings' method ..."
''', label="Anwendung")
S += callout("tipp", [
    "Sicherheitsrelevante Abweichungen gehören als <b>Warnung</b> ins Log und nicht als "
    "Debug-Meldung. Teams mit strengen Anforderungen eskalieren sie per " +
    c("ConfigureWarnings") + " zu Fehlern; andere sehen sie zumindest beim Start. Das "
    "Plugin muss diese Entscheidung nicht für alle treffen.",
])
