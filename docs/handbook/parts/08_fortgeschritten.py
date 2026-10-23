# ============================================================ TEIL VIII
S += part(
    "Fortgeschrittene Themen",
    "Grenzen der Laufzeit-Haken, weitere Erweiterungspunkte, Betrieb in Produktion und "
    "der Weg vom Plugin in die Anwendung.",
    ["Kapitel 27: Massenoperationen und die Grenzen von Interceptors",
     "Kapitel 28: Value Generation",
     "Kapitel 29: Die Update-Pipeline",
     "Kapitel 30: Complex Types und JSON",
     "Kapitel 31: Modelle pro Laufzeitkontext",
     "Kapitel 32: Migrations für Fortgeschrittene",
     "Kapitel 33: Row Level Security in Produktion",
     "Kapitel 34: Performance von Plugins",
     "Kapitel 35: Einsatz in ASP.NET Core",
     "Kapitel 36: CI und API-Stabilität"])

# ------------------------------------------------------------ 27
S += h1("Massenoperationen und die Grenzen von Interceptors")
S.append(p(
    "Ein SaveChanges-Interceptor sieht nur, was über " + c("SaveChanges") + " läuft. "
    + c("ExecuteUpdate") + " und " + c("ExecuteDelete") + " schreiben direkt in die "
    "Datenbank, ohne Change Tracker und ohne SaveChanges. Dasselbe gilt für rohes SQL. Ein "
    "Plugin, das seine Garantien nur über SaveChanges durchsetzt, hat hier eine Lücke:"))
S += data_table(
    ["Weg", "SaveChanges-Interceptor", "Connection-Interceptor", "RLS"],
    [
        [c("SaveChanges"), "ja", "ja", "ja"],
        [c("ExecuteUpdate") + ", " + c("ExecuteDelete"), "<b>nein</b>", "ja", "ja"],
        [c("FromSql") + ", " + c("SqlQuery") + ", " + c("ExecuteSql"), "<b>nein</b>", "ja", "ja"],
        ["ADO.NET oder psql als App-Rolle", "<b>nein</b>", "<b>nein</b>",
         "ja (ohne Mandant: keine Zeilen)"],
    ], [1.6, 1.1, 1.1, 1.2])
S.append(p(
    "Im Beispiel fängt RLS jeden dieser Wege ab, weil die Datenbank die Regeln selbst "
    "durchsetzt. Ein Integrationstest beweist das, indem er den Interceptor gezielt umgeht:"))
S += code_block('''
[Fact]
public async Task ExecuteUpdate_bypasses_interceptor_but_not_rls()
{
    // ... Bestellung für "tenant" anlegen ...
    await using var attacker = ContextFor(tenant);

    // Kein SaveChanges -> der SaveChanges-Interceptor läuft NICHT.
    // WITH CHECK der Policy verhindert das Umhängen trotzdem.
    var ex = await Assert.ThrowsAsync<PostgresException>(() =>
        attacker.Orders.ExecuteUpdateAsync(
            s => s.SetProperty(o => o.TenantId, TenantId.New())));

    Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
}
''', label="tests/...IntegrationTests/AdvancedTests.cs (Auszug)")
S += callout("wichtig", [
    "Plugins ohne Datenbank-Absicherung (Soft Delete, Audit-Felder, Mandantenfilter nur "
    "per Query-Filter) müssen dokumentieren, dass Massenoperationen und rohes SQL ihre "
    "Regeln umgehen. Wer das verhindern will, kann einen " + c("IQueryExpressionInterceptor") +
    " einsetzen: " + c("ExecuteUpdate") + " und " + c("ExecuteDelete") + " laufen durch die "
    "Query-Pipeline, der Interceptor sieht ihren Ausdrucksbaum und kann etwa ein " +
    c("SetProperty") + " auf geschützte Spalten ablehnen. Rohes SQL bleibt trotzdem offen.",
], title="Was ein Plugin dokumentieren muss")

# ------------------------------------------------------------ 28
S += h1("Value Generation")
S.append(p(
    "EF erzeugt Schlüsselwerte entweder in der Datenbank (Identity, Sequenzen) oder "
    "clientseitig über einen " + c("ValueGenerator") + ". Für " + c("Guid") + "-Schlüssel "
    "macht EF das von selbst, für eigene Typen wie " + c("TenantId") + " nicht. Ein Plugin "
    "schließt diese Lücke mit einem Generator, einer Factory und einer Convention:"))
S += code_block('''
public class TenantIdValueGenerator : ValueGenerator<TenantId>
{
    // Endgültige Werte, keine temporären Platzhalter bis zum Speichern
    public override bool GeneratesTemporaryValues => false;

    public override TenantId Next(EntityEntry entry) => TenantId.New();   // UUIDv7
}
''', label="ValueGeneration/TenantIdValueGenerator.cs (Kernpaket)")
S += code_block('''
public class TenantIdValueGeneratorFactory : ValueGeneratorFactory
{
    // Zustandsloser Generator: EF darf ihn cachen und zwischen Contexts teilen
    public override ValueGenerator Create(IProperty property, ITypeBase typeBase)
        => new TenantIdValueGenerator();
}
''', label="ValueGeneration/TenantIdValueGeneratorFactory.cs (Kernpaket)")
S += code_block('''
public virtual void ProcessModelFinalizing(
    IConventionModelBuilder modelBuilder,
    IConventionContext<IConventionModelBuilder> context)
{
    foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
    {
        if (entityType.BaseType is not null
            || entityType.FindPrimaryKey() is not { Properties: [var property] }
            || property.ClrType != typeof(TenantId))
            continue;

        // Convention-Quelle: ein explizites ValueGeneratedNever() gewinnt (-> null)
        if (property.Builder.ValueGenerated(ValueGenerated.OnAdd) is null)
            continue;

        property.Builder.HasValueGeneratorFactory(typeof(TenantIdValueGeneratorFactory));
    }
}
''', label="Conventions/TenantIdKeyValueGenerationConvention.cs (Kernpaket)")
S += callout("wichtig", [
    "Einen Value Generator kann man auf mehreren Wegen konfigurieren: " +
    c("HasValueGenerator") + " mit einem Typ oder einer Factory-Funktion, oder " +
    c("HasValueGeneratorFactory") + " mit einem Factory-Typ. Der Generator für Compiled "
    "Models in EF Core 10 akzeptiert nur die letzte Variante und bricht sonst mit „The "
    "property ... has a value generator configured. Use 'HasValueGeneratorFactory' to "
    "configure the value generator factory type.“ ab. Plugins sollten deshalb " +
    c("HasValueGeneratorFactory(Type)") + " verwenden und der Factory einen öffentlichen "
    "parameterlosen Konstruktor geben.",
], title="Factory-Typ statt Generator")
S.append(p(
    "Die Convention liegt im Kernpaket, weil sie nichts mit PostgreSQL zu tun hat; das "
    "Provider-Paket hängt sie über sein Convention-Set-Plugin ein. Der Compiled-Model-Test "
    "prüft, dass die Factory im erzeugten Code auftaucht."))

# ------------------------------------------------------------ 29
S += h1("Die Update-Pipeline")
S.append(p(
    "Kapitel 11 beschreibt den Weg von LINQ zu SQL. Für das Schreiben gibt es eine eigene "
    "Pipeline, die Plugins für Bulk-Operationen, datenbankseitige Soft-Deletes oder "
    "zurückgelieferte Werte erweitern:"))
S += flow_diagram([
    ("ChangeTracker", "Ermittelt hinzugefügte, geänderte und gelöschte Entitäten"),
    ("IModificationCommandFactory", "Ein ModificationCommand pro Zeile und Tabelle"),
    ("ICommandBatchPreparer", "Sortiert nach Abhängigkeiten (Fremdschlüssel) und bildet Batches"),
    ("IModificationCommandBatchFactory", "Erzeugt die Batches (Größe, Provider-Besonderheiten)"),
    ("IUpdateSqlGenerator", "Schreibt INSERT, UPDATE, DELETE, ggf. mit RETURNING"),
    ("Ausführung", "Ergebnisse (Identity, Concurrency-Token) zurück in die Entitäten"),
])
S += data_table(
    ["Dienst", "Lebensdauer", "Art"],
    [
        [c("IModificationCommandFactory"), "Singleton", "einzeln"],
        [c("ICommandBatchPreparer"), "Scoped", "einzeln"],
        [c("IModificationCommandBatchFactory"), "Scoped", "einzeln"],
        [c("IUpdateSqlGenerator"), "Singleton", "einzeln"],
    ], [2.4, 1, 1])
S.append(p(
    "Alle Dienste sind Einzeldienste des Providers, mit den Konsequenzen aus Kapitel 5. "
    "Bevor man sie ersetzt, lohnt die Prüfung der Alternativen: Ein SaveChanges-Interceptor "
    "kann Zustände umschreiben (aus Löschen wird Ändern beim Soft Delete), ein "
    "Command-Interceptor kann SQL ergänzen, und für Massenänderungen gibt es " +
    c("ExecuteUpdate") + ". Ein eigener " + c("IUpdateSqlGenerator") + " ist erst dann "
    "gerechtfertigt, wenn das SQL selbst eine andere Form braucht, etwa " +
    c("INSERT ... ON CONFLICT") + " für Upserts."))

# ------------------------------------------------------------ 30
S += h1("Complex Types und JSON")
S += h2("Complex Types")
S.append(p(
    "Complex Types (seit EF Core 8) sind Werttypen ohne eigene Identität, deren Properties "
    "in der Tabelle des Besitzers landen. Für Plugins heißt das: Conventions, die über " +
    c("entityType.GetProperties()") + " laufen, sehen deren Properties <b>nicht</b>. Sie "
    "hängen an " + c("IComplexProperty") + " und müssen eigens durchlaufen werden:"))
S += code_block('''
foreach (var complexProperty in entityType.GetComplexProperties())
{
    foreach (var property in complexProperty.ComplexType.GetProperties())
    {
        // Spaltennamen sind präfixiert (z. B. "Address_City"); immer über
        // property.GetColumnName(table) ermitteln, nie selbst zusammensetzen
    }
}
''', label="Muster für Conventions (Skizze)")
S.append(p(
    "Fluent-API-Methoden für Properties sollten zusätzlich auf " +
    c("ComplexTypePropertyBuilder") + " angeboten werden, wenn sie auch dort sinnvoll sind. "
    "Das Beispiel-Plugin braucht das nicht: Isolation und Wertgenerierung betreffen nur "
    "Entitäten."))

S += h2("JSON-Spalten")
S.append(p(
    "Owned Types und (seit EF Core 10) Complex Types lassen sich mit " + c("ToJson()") +
    " als eine JSON-Spalte speichern. Werte darin liest und schreibt EF nicht über "
    "Konverter, sondern über den " + c("JsonValueReaderWriter") + " des Type Mappings "
    "(Kapitel 10). Fehlt er, scheitert der Modellbau. Unit- und Integrationstest des "
    "Beispiels prüfen beide Seiten:"))
S += code_block('''
using var ctx = Contexts.Create(model: m =>
    m.Entity<Shipment>().OwnsOne(s => s.Details, d => d.ToJson()));

var property = ctx.DesignModel()
    .FindEntityType(typeof(ShipmentDetails))!
    .FindProperty(nameof(ShipmentDetails.ApprovedBy))!;

Assert.Same(TenantIdJsonReaderWriter.Instance,
    property.GetTypeMapping().JsonValueReaderWriter);
''', label="tests/.../JsonTests.cs")
S += callout("hinweis", [
    "PostgreSQL-Arrays (" + c("List<TenantId>") + " als " + c("uuid[]") + ") sind kein "
    "JSON: Npgsql bildet primitive Collections nativ als Arrays ab. Ob ein Provider "
    "Collections mit konvertierten Elementtypen unterstützt, ist providerspezifisch und "
    "gehört in einen eigenen Test, bevor man es dokumentiert.",
])

# ------------------------------------------------------------ 31
S += h1("Modelle pro Laufzeitkontext")
S.append(p(
    "EF baut das Modell einmal pro Context-Typ und cached es (Kapitel 2). Soll das Modell "
    "von Laufzeitdaten abhängen, etwa einem Schema pro Mandant, muss der Cache-Schlüssel "
    "diese Daten enthalten. Dafür gibt es " + c("IModelCacheKeyFactory") + ":"))
S += code_block('''
public sealed class SchemaModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
        => context is ISchemaScoped scoped
            ? (context.GetType(), scoped.Schema, designTime)
            : (object)(context.GetType(), designTime);
}

public sealed class SchemaContext : DbContext, ISchemaScoped
{
    // Konstruktor: (DbContextOptions<SchemaContext> options, string schema)
    public string Schema { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.HasDefaultSchema(Schema);
}

// Registrierung
options.UseNpgsql(connectionString)
       .ReplaceService<IModelCacheKeyFactory, SchemaModelCacheKeyFactory>();
''', label="tests/.../SchemaPerTenantTests.cs (gekürzt)")
S.append(p(
    "Der Test " + c("SchemaPerTenantTests") + " prüft beide Seiten des Cache-Schlüssels: "
    "Zwei Schemas ergeben zwei Modelle mit jeweils eigenem Standardschema, und dasselbe "
    "Schema verwendet das gecachte Modell wieder, statt es neu zu bauen."))
S.append(p(
    "Das ist die Grundlage für „Schema pro Mandant“, eine Alternative zu RLS. Die drei "
    "üblichen Ansätze im Vergleich:"))
S += data_table(
    ["Ansatz", "Stärken", "Schwächen"],
    [
        ["Gemeinsame Tabellen + RLS (Beispiel)", "Ein Modell, ein Schema, einfache "
         "Migrationen; Trennung erzwingt die Datenbank", "Alle Mandanten teilen Tabellen und "
         "Indizes; Fehlkonfiguration der Rollen hebelt die Trennung aus"],
        ["Schema pro Mandant", "Starke Trennung, getrennte Backups pro Schema möglich",
         "Ein Modell pro Mandant im Cache (Speicher!), Migrationen für jedes Schema"],
        ["Datenbank pro Mandant", "Maximale Trennung, unabhängige Skalierung",
         "Ein Modell genügt, aber Verbindungen, Migrationen und Betrieb pro Datenbank"],
    ], [1.3, 1.6, 1.8])
S += callout("wichtig", [
    "Jeder Schlüsselwert erzeugt ein eigenes Modell im Speicher. Bei Hunderten Mandanten "
    "mit großen Modellen wird das teuer. Außerdem dürfen Werte, die das Modell verändern, "
    "nie aus ungeprüften Eingaben stammen.",
], title="Modell-Cache und Speicher")

# ------------------------------------------------------------ 32
S += h1("Migrations für Fortgeschrittene")
S += h2("Reihenfolge von Operationen")
S.append(p(
    "Generierte und handgeschriebene Operationen laufen in der Reihenfolge, in der sie in "
    + c("Up()") + " stehen. Das ist eine echte Fehlerquelle. Die Policy " +
    c("support_read") + " des Beispiels gilt " +
    c("TO shop_support") + ". EF erzeugt sie direkt nach " + c("CREATE TABLE") + ". Wird "
    "die Rolle erst am Ende von " + c("Up()") + " angelegt, scheitert die Migration."))
S += code_block('''
protected override void Up(MigrationBuilder migrationBuilder)
{
    // 1. Was Policies referenzieren: VOR den generierten Operationen
    migrationBuilder.CreateApplicationRole("shop_support");

    // 2. Von EF generiert: Tabellen, RLS, Policies (TO shop_support)
    migrationBuilder.CreateTable(name: "Customers", /* ... */);

    // 3. Was Tabellen voraussetzt: NACH den generierten Operationen
    migrationBuilder.CreateApplicationRole("shop_app", canLogin: true);
    migrationBuilder.GrantTablePrivileges("shop_app");     // wirkt auf bestehende Tabellen
}
''', label="Migrations/20261021_Initial.cs")

S += h2("Idempotente Skripte")
S.append(p(
    c("dotnet ef migrations script --idempotent") + " erzeugt ein Skript, das gegen jeden "
    "Datenbankstand laufen kann. Npgsql verpackt dazu jede Operation in einen Block "
    + c("DO $EF$ ... $EF$") + ", der die Historientabelle prüft. Eigene Operationen, die "
    "selbst einen " + c("DO $$") + "-Block erzeugen, stecken dann darin. Das funktioniert, "
    "weil die Dollar-Quotes verschiedene Tags haben. Solche Annahmen gehören in einen Test:"))
S += code_block('''
var script = db.GetService<IMigrator>()
    .GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

await db.Database.ExecuteSqlRawAsync(script);   // verschachteltes DO gültig?
await db.Database.ExecuteSqlRawAsync(script);   // zweiter Lauf wird übersprungen
''', label="tests/...IntegrationTests/AdvancedTests.cs (Auszug)")

S += h2("Operationen außerhalb von Transaktionen")
S.append(p(
    "Manche Befehle dürfen nicht in einer Transaktion laufen, in PostgreSQL etwa " +
    c("CREATE INDEX CONCURRENTLY") + ". Eigene Operationen mit solchem SQL beenden ihren "
    "Befehl mit " + c("EndStatement(builder, suppressTransaction: true)") + ". EF führt "
    "ihn dann außerhalb der Migrationstransaktion aus. Der Preis: Schlägt die Migration "
    "danach fehl, wird dieser Befehl nicht zurückgerollt; er sollte deshalb idempotent sein."))

S += h2("Migration Bundles")
S.append(p(
    c("dotnet ef migrations bundle") + " erzeugt eine ausführbare Datei, die Migrationen "
    "ohne SDK anwendet. Sie enthält das Startprojekt samt Plugin-Assemblies; eigene "
    "Operationen und der SQL-Generator des Plugins wirken also auch dort. Wichtig ist, dass "
    "die Design-Time-Factory oder der Host dieselben Plugin-Optionen verwendet wie die "
    "Anwendung, sonst entsteht SQL für ein anderes Modell."))

# ------------------------------------------------------------ 33
S += h1("Row Level Security in Produktion")
S.append(p(
    "RLS ist nur so stark wie seine Betriebsbedingungen. Diese Fallen machen die Policies "
    "stillschweigend wirkungslos oder unbrauchbar:"))
S += data_table(
    ["Falle", "Folge", "Abhilfe"],
    [
        ["App läuft als Tabelleneigentümer", "Policies werden ignoriert",
         c("FORCE ROW LEVEL SECURITY") + " (Default im Plugin) und getrennte Rollen"],
        ["Superuser oder " + c("BYPASSRLS"), "RLS wird immer umgangen, auch mit FORCE",
         "App-Rolle mit " + c("NOSUPERUSER NOBYPASSRLS") + " (eigene Operation, Kap. 19)"],
        ["Setting nicht gesetzt", "Ohne " + c("nullif") + ": Fehler; mit: leere Ergebnisse",
         "Interceptor setzt den Wert bei jedem Öffnen"],
        ["Kein Index auf der Mandantenspalte", "Jede Abfrage scannt die ganze Tabelle",
         "Index-Convention (Kapitel 8)"],
        ["Transaction-Pooling (PgBouncer)", "Session-Settings landen bei anderen Clients "
         "oder gehen verloren", "Setting transaktionslokal setzen (siehe unten)"],
        ["Verbindung außerhalb von EF geöffnet", "Die Session behält einen alten Mandanten "
         "oder keinen", "EF die Verbindungen öffnen lassen oder " +
         c("ApplyTenantSession()") + " (Kapitel 15)"],
        ["„Bypass“-Schalter für die App-Rolle", "Wer SQL als App-Rolle ausführen kann, kann "
         "ihn umlegen", "Eigene Rollen für mandantenübergreifenden Zugriff (siehe unten)"],
    ], [1.3, 1.3, 1.6])
S += h2("Transaction-Pooling")
S.append(p(
    "PgBouncer im Modus „transaction“ teilt eine Serververbindung zwischen Clients pro "
    "Transaktion. Ein mit " + c("set_config(..., false)") + " gesetzter Session-Wert kann "
    "dann bei einem anderen Client ankommen, eine Sicherheitslücke. Die sichere Variante: "
    "das Setting mit " + c("set_config(..., true)") + " <i>transaktionslokal</i> setzen und "
    "jede Datenbankarbeit in einer expliziten Transaktion ausführen. Technisch wechselt der "
    "Interceptor dafür von " + c("IDbConnectionInterceptor") + " zu " +
    c("IDbTransactionInterceptor") + " (" + c("TransactionStarted") + "). Weil dann Abfragen "
    "ohne Transaktion gar keinen Mandanten haben, muss die Anwendung das konsequent "
    "einhalten. Das Beispiel bleibt beim einfacheren Session-Modell und dokumentiert, dass "
    "es Session- oder Npgsql-Pooling voraussetzt."))
S += h2("Performance von Policies")
S.append(p(
    "PostgreSQL fügt Policies wie zusätzliche " + c("WHERE") + "-Bedingungen in jede "
    "Abfrage ein. " + c("current_setting") + " ist " + c("STABLE") + " und wird pro "
    "Abfrage einmal ausgewertet; zusammen mit dem Index ist der Aufwand gering. Teure "
    "Policies entstehen durch Unterabfragen (etwa Rechte aus einer anderen Tabelle) oder "
    "Funktionen, die nicht " + c("LEAKPROOF") + " sind und Optimierungen verhindern. "
    "Prüfen lässt sich das mit " + c("EXPLAIN (ANALYZE)") + " als App-Rolle, nicht als Owner, "
    "weil der Owner die Policies sonst gar nicht sieht."))

S += h2("Legitimer mandantenübergreifender Zugriff")
S.append(p(
    "Früher oder später braucht jemand Daten über Mandanten hinweg: Berichte für den "
    "Betreiber, Wartungsjobs, Support. Die verlockende Abkürzung ist ein Schalter in der "
    "Anwendung, etwa ein zusätzliches Session-Setting " + c("app.bypass") + ", das die "
    "Policy berücksichtigt. Das ist keine Sicherheitsgrenze: Was die Anwendungsrolle setzen "
    "kann, kann auch ein eingeschleustes SQL-Statement setzen. Die Trennung muss stattdessen "
    "über Datenbankrollen entstehen:"))
S += data_table(
    ["Bedarf", "Lösung", "Warum es sicher ist"],
    [
        ["Berichte, Analysen", "Eigene Nur-Lese-Rolle mit " + c("BYPASSRLS") + ", nur " +
         c("SELECT") + ", eigenem Connection-String und einer Context-Konfiguration ohne "
         "Mandant", "Die App-Rolle kann nicht zu dieser Rolle werden; ihre Zugangsdaten "
         "liegen woanders"],
        ["Support liest ausgewählte Daten", "Zusätzliche permissive Policy für eine "
         "Support-Rolle: " + c("HasPolicy(...)") + " mit " + c("For(Select)") + " und " +
         c("To(\"shop_support\")"),
         "Beschränkt auf SELECT und auf Mitglieder der Rolle"],
        ["Wartung pro Mandant", "Hintergrundjob, der die Mandanten durchläuft und jeden "
         "einzeln setzt (Kapitel 35)", "Läuft durch die normalen Policies"],
    ], [1.2, 2, 1.6])
S += code_block('''
-- Angelegt von einem Administrator: BYPASSRLS erfordert einen Superuser
CREATE ROLE shop_reporting LOGIN PASSWORD '...' NOSUPERUSER BYPASSRLS;
GRANT USAGE ON SCHEMA public TO shop_reporting;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO shop_reporting;
''', lang="sql", label="Reporting-Rolle (tests/...IntegrationTests/CrossTenantAccessTests.cs)")
S.append(p(
    "Die Anwendungsrolle dagegen kann die Policies nicht abschalten. " +
    c("SET row_security = off") + " deaktiviert RLS für eine Rolle ohne " +
    c("BYPASSRLS") + " nicht; PostgreSQL lehnt stattdessen jede Abfrage ab, die die "
    "Policies betreffen würden. " + c("CrossTenantAccessTests") + " prüft alle drei Punkte: "
    "Die Reporting-Rolle sieht alle Mandanten, sie kann nicht schreiben, und die App-Rolle "
    "kann sich nicht selbst helfen."))
S += callout("wichtig", [
    "Jede Verbindung der Reporting-Rolle ist ein Zugriff auf alle Mandanten. Sie gehört "
    "protokolliert (" + c("log_connections") + ", pgaudit), und die Rolle sollte so wenigen "
    "Personen und Diensten wie möglich zugewiesen werden. " + c("CreateApplicationRole") +
    " des Plugins legt Rollen bewusst immer mit " + c("NOBYPASSRLS") + " an: "
    "Bypass-Rollen gehören nicht in die Migrationen der Anwendung.",
], title="Mandantenübergreifenden Zugriff protokollieren")

# ------------------------------------------------------------ 34
S += h1("Performance von Plugins")
S.append(p(
    "Plugin-Code läuft in drei Häufigkeiten. Wer weiß, welcher Code wie oft läuft, weiß, wo "
    "sich Optimierung lohnt:"))
S += data_table(
    ["Häufigkeit", "Code", "Folgerung"],
    [
        ["Einmal pro Provider", "Conventions, Type-Mapping-Plugins, Singleton-Options",
         "Lesbarkeit vor Mikro-Optimierung"],
        ["Einmal pro Abfrageform", "Translators, Evaluatable-Filter, Query-Interceptors",
         "Moderat; Ergebnis wird gecacht"],
        ["Bei jeder Ausführung", "Interceptors für Commands, Verbindungen, SaveChanges, "
         "Materialisierung", "Heißer Pfad: keine Reflection, keine unnötigen Allokationen"],
    ], [1.2, 2, 1.6])
S.append(p(
    "Daraus ergeben sich einige Regeln, die das Beispiel befolgt: " + c("MethodInfo") +
    "- und " + c("PropertyInfo") + "-Lookups liegen in statischen Feldern. Der "
    "SaveChanges-Interceptor legt seine Liste erst an, wenn er tatsächlich eine "
    "mandantenfähige Änderung findet. Der Connection-Interceptor kostet einen Roundtrip pro "
    "Öffnen, was dokumentiert ist. Solche Annahmen sollte man messen, nicht raten. Das "
    "Repository enthält ein BenchmarkDotNet-Projekt für die SaveChanges-Logik; es läuft "
    "ohne Datenbank:"))
S += code_block('''
[MemoryDiagnoser]
public class SaveChangesInterceptorBenchmark
{
    [Params(1, 100)]
    public int Entities { get; set; }

    // GlobalSetup: Context mit UseAcmeTenancy() (ohne Verbindung) und dem Interceptor

    [Benchmark(Baseline = true)]
    public int TrackOnly()                  // was EF vor dem Speichern ohnehin tut
    {
        AddOrders();
        var count = _db.ChangeTracker.Entries().Count();
        _db.ChangeTracker.Clear();
        return count;
    }

    [Benchmark]
    public int TrackAndApplyTenant()        // dasselbe plus die SaveChanges-Logik des Plugins
    {
        AddOrders();
        _interceptor.ApplyTenant(_db);
        var count = _db.ChangeTracker.Entries().Count();
        _db.ChangeTracker.Clear();
        return count;
    }
}
''', label="benchmarks/.../SaveChangesInterceptorBenchmark.cs (gekürzt)")
S += code_block('''
dotnet run -c Release --project benchmarks/Acme.EntityFrameworkCore.Tenancy.Benchmarks
''', lang="text", label="Benchmark ausführen")
S += data_table(
    ["Methode", "Entitäten", "Mittelwert", "Verhältnis", "Allokiert"],
    [
        [c("TrackOnly"), "1", "1,5 µs", "1,00", "1,8 KB"],
        [c("TrackAndApplyTenant"), "1", "2,3 µs", "1,54", "2,7 KB"],
        [c("TrackOnly"), "100", "123 µs", "1,00", "134 KB"],
        [c("TrackAndApplyTenant"), "100", "170 µs", "1,38", "202 KB"],
    ], [2, 1, 1, 1, 1])
S.append(p(
    "Beispielergebnisse (Short Job, .NET 10, Rechner des Autors; die absoluten Werte "
    "schwanken, aussagekräftiger sind die Verhältnisse). Das Plugin fügt pro gespeicherter "
    "Entität etwa eine halbe Mikrosekunde hinzu, rund 40 bis 50 Prozent mehr als das "
    "Change Tracking allein. Das klingt nach viel, doch ein einziger Roundtrip zur "
    "Datenbank kostet Hunderte Mikrosekunden; der zusätzliche Roundtrip des "
    "Connection-Interceptors pro Öffnen (Kapitel 15) wiegt daher weit schwerer als die "
    "SaveChanges-Logik. Genau diese Art Erkenntnis liefert ein Benchmark: Er zeigt, wo sich "
    "Optimieren lohnt und wo nicht."))
S += callout("tipp", [
    "Für den Start großer Modelle ist das Compiled Model (Kapitel 21) der wirksamste Hebel. "
    "Conventions eines Plugins laufen dann gar nicht mehr zur Laufzeit; deshalb lohnt es "
    "sich, die Compiled-Model-Fähigkeit zu testen statt nur zu behaupten.",
])

# ------------------------------------------------------------ 35
S += h1("Einsatz in ASP.NET Core")
S.append(p(
    "Das Beispiel " + c("samples/ShopApi") + " zeigt den Weg vom Plugin in eine Web-API. "
    "Der Mandant kommt aus dem Claim " + c("tenant_id") + " des angemeldeten Benutzers:"))
S += code_block('''
public sealed class HttpTenantProvider : ITenantProvider
{
    public const string ClaimType = "tenant_id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpTenantProvider(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        _httpContextAccessor = httpContextAccessor;
    }

    // Bei jedem Zugriff neu ermittelt: funktioniert auch als Singleton (Pooling)
    public TenantId? CurrentTenantId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimType)?.Value;
            return Guid.TryParse(value, out var id) ? new TenantId(id) : null;
        }
    }
}
''', label="samples/ShopApi/HttpTenantProvider.cs")
S += code_block('''
builder.Services.AddHttpContextAccessor();
builder.Services.AddAcmeTenancy<HttpTenantProvider>();
builder.Services.AddDbContext<ShopApiContext>((sp, options) => options
    .UseNpgsql(builder.Configuration.GetConnectionString("Shop"))
    .UseAcmeTenancy(sp));

// Kein Mandantenfilter im Code: RLS in der Datenbank sorgt für die Trennung
app.MapGet("/orders", async (ShopApiContext db) =>
    await db.Orders.OrderBy(o => o.Id).ToListAsync());
''', label="samples/ShopApi/Program.cs (Auszug)")
S += callout("wichtig", [
    "Der Mandant muss aus einer vertrauenswürdigen Quelle kommen: einem Claim aus einem "
    "signierten Token. Ein Header oder Query-Parameter, den der Client frei setzt, hebelt "
    "die gesamte Isolation aus. Das Beispiel enthält eine Header-Simulation nur für die "
    "Entwicklungsumgebung und kennzeichnet sie ausdrücklich.",
], title="Woher der Mandant kommt")
S.append(p(
    "Für " + c("AddDbContextPool") + " registriert man den Provider als Singleton. Das ist "
    "möglich, weil " + c("HttpTenantProvider") + " den Mandanten bei jedem Zugriff neu "
    "ermittelt; die Interceptors fragen ihn bei jeder Verbindung und jedem Speichern:"))
S += code_block('''
builder.Services.AddAcmeTenancy<HttpTenantProvider>(ServiceLifetime.Singleton);
builder.Services.AddDbContextPool<ShopApiContext>((sp, options) => options
    .UseNpgsql(connectionString)
    .UseAcmeTenancy(sp));   // sp: Root-Provider; Interceptors entstehen einmal
''', label="Variante mit Pooling")


S += h2("DbContext-Factories")
S.append(p(
    c("AddDbContextFactory") + " ist die übliche Wahl in Blazor Server und für parallele "
    "Arbeit. Anders als " + c("AddDbContext") + " registriert es Factory und Options "
    "standardmäßig als <b>Singletons</b>. Der Options-Callback bekommt dann den "
    "Root-Provider, nicht den Scope der aktuellen Anfrage. Für einen Scoped-Tenant-Provider, "
    "der Zustand pro Scope hält, hat das Folgen:"))
S += data_table(
    ["Registrierung", "Verhalten"],
    [
        ["Singleton-Factory, Scope-Validierung an (ASP.NET Core in Development)",
         "Exception: „Cannot resolve scoped service 'ITenantProvider' from root provider.“"],
        ["Singleton-Factory, Scope-Validierung aus (Standard in Produktion)", "Kein Fehler. "
         "Die Interceptors bekommen eine Instanz aus dem Root-Container, die keine Anfrage "
         "je befüllt: kein Mandant, Speichern wirft, Abfragen sehen nichts"],
        ["Factory als " + c("Scoped") + " registriert", "Jeder Scope bekommt eine eigene "
         "Factory mit eigenem Tenant-Provider: korrekt"],
    ], [2, 2.6])
S += code_block('''
builder.Services.AddDbContextFactory<ShopApiContext>(
    (sp, options) => options.UseNpgsql(connectionString).UseAcmeTenancy(sp),
    ServiceLifetime.Scoped);         // Factory und Options pro Scope
''', label="Factory mit dem Mandanten der Anfrage")
S.append(p(
    "Ein Provider, der den Mandanten bei jedem Zugriff neu ermittelt, etwa " +
    c("HttpTenantProvider") + ", funktioniert in jeder Variante; das Problem betrifft "
    "Provider mit Zustand pro Scope. " + c("AddPooledDbContextFactory") + " verhält sich "
    "immer wie Pooling: Die Options existieren einmal, der Tenant-Provider muss also ein "
    "solches Singleton sein. " + c("DbContextFactoryTests") + " zeigt alle drei Varianten."))

S += h2("Außerhalb von HTTP-Anfragen")
S.append(p(
    "Hintergrunddienste, Message-Handler und geplante Jobs haben keine Anfrage und keinen "
    "Claim. Der Mandant kommt dann aus dem Job selbst, etwa aus der Nachricht oder aus einer "
    "Liste zu verarbeitender Mandanten. Der " + c("AmbientTenantProvider") + " des Beispiels "
    "speichert ihn in einem " + c("AsyncLocal") + ", das mit dem asynchronen Kontrollfluss "
    "mitwandert:"))
S += code_block('''
public sealed class AmbientTenantProvider : ITenantProvider
{
    private static readonly AsyncLocal<TenantId?> Current = new();

    public TenantId? CurrentTenantId => Current.Value;

    public static IDisposable Use(TenantId tenant)
    {
        var previous = Current.Value;
        Current.Value = tenant;
        return new Restore(previous);     // setzt beim Dispose den vorigen Mandanten zurück
    }

    // private sealed class Restore : IDisposable { ... Current.Value = _previous; }
}
''', label="samples/ShopSample/AmbientTenantProvider.cs (gekürzt)")
S += code_block('''
// Erst der Mandant, dann Scope und Context: die Interceptors entstehen im Scope
using (AmbientTenantProvider.Use(tenant))
{
    await using var scope = scopeFactory.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ShopContext>();
    // ... Arbeit für diesen Mandanten ...
}
''', label="Job-Muster (tests/.../BackgroundTenantTests.cs)")
S.append(p(
    "Weil der Provider das " + c("AsyncLocal") + " bei jedem Zugriff liest, kann er als "
    "Singleton registriert werden. Parallele Jobs behalten ihren eigenen Mandanten, und nach "
    "dem " + c("using") + "-Block bleibt kein Mandant zurück; " + c("BackgroundTenantTests") +
    " prüft beides mit 20 parallelen Jobs. Eine Anwendung mit Web-Anfragen <i>und</i> Jobs "
    "kombiniert beide Quellen in einem Provider: zuerst der Claim, sonst der Ambient-Mandant."))
S += callout("wichtig", [
    "Ohne Mandant muss ein Job scheitern oder nichts sehen, niemals alles. Das Plugin "
    "verhält sich so: Der Session-Wert bleibt leer, die Policies treffen keine Zeile, und "
    "Speichern wirft. Jobs, die wirklich alle Mandanten brauchen, nutzen die Reporting-Rolle "
    "aus Kapitel 33.",
], title="Nie auf „alle Mandanten“ zurückfallen")

# ------------------------------------------------------------ 36
S += h1("CI und API-Stabilität")
S += h2("Der Workflow")
S.append(p(
    "Der GitHub-Actions-Workflow im Repository setzt die Empfehlungen aus Kapitel 23 und 24 um. "
    "Ein Job baut und testet ohne und mit Datenbank; ein zweiter baut das Handbuch; "
    "ein dritter baut gegen die nächste EF-Preview und darf fehlschlagen:"))
S += code_block('''
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { global-json-file: global.json }
      - run: dotnet build -c Release
      - run: dotnet test tests/Acme.EntityFrameworkCore.Tenancy.Tests -c Release --no-build
      - run: dotnet test tests/Acme.EntityFrameworkCore.Tenancy.IntegrationTests
          -c Release --no-build
      - run: dotnet test tests/Acme.EntityFrameworkCore.Tenancy.SqlServer.Tests
          -c Release --no-build

  handbook:                            # beide PDFs; scheitert an zu langen Codezeilen
    runs-on: ubuntu-latest
    steps:
      # ... checkout, setup-python, pip install -r docs/handbook/requirements.txt ...
      - run: python docs/handbook/build.py --lang en

  ef-preview:
    runs-on: ubuntu-latest
    continue-on-error: true              # Frühwarnsystem, kein Blocker
    steps:
      # ... setup-dotnet mit 11.0.x preview ...
      - run: dotnet build -c Release -p:EFCorePreview=true
''', lang="text", label=".github/workflows/ci.yml (gekürzt)")
S.append(p(
    "Der Preview-Build funktioniert über bedingte Paketversionen in " +
    c("Directory.Packages.props") + ":"))
S += code_block('''
<PropertyGroup>
  <CentralPackageFloatingVersionsEnabled>true</CentralPackageFloatingVersionsEnabled>
</PropertyGroup>
<ItemGroup Condition="'$(EFCorePreview)' != 'true'">
  <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="[10.0.0,11.0.0)" />
</ItemGroup>
<ItemGroup Condition="'$(EFCorePreview)' == 'true'">
  <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="11.0.0-*" />
</ItemGroup>
''', lang="xml", label="Directory.Packages.props (Auszug)")

S += h2("Öffentliche API absichern")
S.append(p(
    "Ein Plugin hat zwei öffentliche APIs: die für Anwender (Fluent API, Options, Typen) und "
    "die für Erweiterer (virtuelle Methoden, Dependencies, öffentliche Dienste). Beide "
    "brechen leicht unbemerkt. Der " + c("Microsoft.CodeAnalysis.PublicApiAnalyzers") +
    " führt die öffentliche API in zwei Textdateien (" + c("PublicAPI.Shipped.txt") + ", " +
    c("PublicAPI.Unshipped.txt") + ") und meldet jede Abweichung. Jede API-Änderung wird "
    "so im Pull Request als Diff sichtbar."))
S += callout("hinweis", [
    "Beim ersten Build meldet der Analyzer jede öffentliche API als RS0016, weil die Dateien "
    "leer sind. Per Code-Fix „Add all items … to the public API“ werden sie gefüllt. Beim "
    "Release wandert der Inhalt von Unshipped nach Shipped; ab dann ist das Entfernen einer "
    "ausgelieferten API ein sichtbarer Breaking Change.",
], title="Einrichtung")
