# ============================================================ Titel & Vorwort
S += title_block(
    "EF Core Plugin-Entwicklung · Referenz und Praxis",
    "Das EF Core Plugin-Handbuch",
    "Von der Options-Extension bis zum NuGet-Paket: alles, was man für professionelle und "
    "auch sehr komplexe Erweiterungen von Entity Framework Core braucht. Durchgängig "
    "erklärt an einem vollständigen Plugin für Mandantenfähigkeit mit PostgreSQL und SQL Server.",
    [
        ("Versionen", "EF Core 10, Npgsql.EntityFrameworkCore.PostgreSQL 10, .NET 10"),
        ("Beispiel", "Vollständige Solution unter "
                     "<b>github.com/guenni81/EF-Core-Plugin-Handbook</b>: "
                     "Kernpaket, zwei Provider-Pakete, zwei Beispiel-Apps, Unit- und Integrationstests"),
        ("Geprüft", "Der Beispielcode wird gegen EF Core 10 und Npgsql 10 gebaut; Unit- "
                    "und Integrationstests laufen gegen echtes PostgreSQL und SQL Server."),
        ("Aufbau", "9 Teile, 42 Kapitel, 4 Anhänge"),
        ("Lizenz", "MIT-Lizenz, © 2026 guenni81. Frei nutzbar, kopierbar, veränderbar und "
                   "weitergebbar, auch kommerziell; siehe letzte Seite."),
    ])

S.append(Paragraph("Über dieses Buch", H2))
S.append(p(
    "Viele Erweiterungen für EF Core beginnen als ein paar Extension-Methods und wachsen dann "
    "unkontrolliert. Wer verstanden hat, wie EF Core intern aufgebaut ist, kann dagegen "
    "Erweiterungen schreiben, die sich wie ein Teil von EF Core anfühlen: Sie werden über "
    "die Options konfiguriert, laufen im internen Service Provider, übersetzen eigene "
    "Funktionen nach SQL, erzeugen Migrationen, schreiben lesbare Snapshots und lassen sich "
    "mit anderen Plugins kombinieren."))
S.append(p(
    "Dieses Buch erklärt jeden dieser Bausteine in zwei Schichten. Zuerst kommt das "
    "<b>Prinzip</b>: Wie funktioniert der Mechanismus in EF Core, welche Varianten gibt es, "
    "worauf muss man achten? Danach folgt die <b>Umsetzung</b> im Beispiel-Plugin, mit dem "
    "Code, der auch im Repository steht. Die Kapitel bauen aufeinander auf, lassen sich "
    "aber auch einzeln als Nachschlagewerk lesen. Die Codeauszüge dieser deutschen Fassung "
    "haben deutsche Kommentare; im Repository sind Kommentare und Meldungen englisch, der "
    "Code ist sonst identisch. Das Label jedes Auszugs nennt die Datei."))
S += data_table(
    ["Kasten", "Bedeutung"],
    [
        ["<font color='#d99a00'><b>Wichtig</b></font>",
         "Fehlerquellen, die zu falschem Verhalten oder Sicherheitslücken führen."],
        ["<font color='#2e8b57'><b>Tipp</b></font>",
         "Bewährte Praxis, die Arbeit spart oder die Qualität erhöht."],
        ["<font color='#1d4e89'><b>Hinweis</b></font>",
         "Hintergrund, Einordnung und Verweise auf andere Kapitel."],
    ], [1, 3])
S += callout("hinweis", [
    "Einige APIs, die Plugins brauchen, liegen in Namespaces mit " + c(".Internal") +
    ". EF Core nennt sie „pubternal“: öffentlich sichtbar, aber ohne Kompatibilitätsgarantie. "
    "Das Buch kennzeichnet diese Stellen und erklärt in Kapitel 24, wie man damit umgeht.",
], title="Öffentliche und interne APIs")

S.append(PageBreak())
S.append(Paragraph("Inhalt", H2))
S.append(toc())

# ============================================================ TEIL I
S += part(
    "Grundlagen",
    "Was ein Plugin ist, wie EF Core intern aufgebaut ist und welches Szenario uns "
    "durch das Buch begleitet.",
    ["Kapitel 1: Was ist ein EF-Core-Plugin?",
     "Kapitel 2: Die Architektur von EF Core aus Plugin-Sicht",
     "Kapitel 3: Das Beispielszenario"])

# ------------------------------------------------------------ 1
S += h1("Was ist ein EF-Core-Plugin?")
S.append(p(
    "Der Begriff „Plugin“ wird für sehr unterschiedliche Dinge verwendet. Für dieses Buch "
    "hilft eine Einteilung in vier Stufen, die sich darin unterscheiden, wie tief sie in "
    "EF Core eingreifen:"))
S += data_table(
    ["Stufe", "Beschreibung", "Beispiele"],
    [
        ["1 · Hilfsmethoden", "Extension-Methods auf " + c("DbContext") + " oder " +
         c("IQueryable") + ", die nur öffentliche API aufrufen.",
         "Paging-Helfer, " + c("WhereIf") + ", Seed-Methoden"],
        ["2 · Modell-Helfer", "Fluent-API-Erweiterungen und Annotations, ohne eigene "
         "Dienste in EF Core.", "Eigene " + c("HasXyz()") + "-Methoden, ModelBuilder-Helfer"],
        ["3 · Echte Plugins", "Eigene Dienste im internen Service Provider: Conventions, "
         "Query-Übersetzung, Type Mappings, Migrations, Design-Time.",
         "NetTopologySuite-Plugin, NodaTime-Plugin, EFCore.NamingConventions"],
        ["4 · Provider", "Komplette Datenbankanbindung inklusive Verbindung, Update-Pipeline "
         "und SQL-Generierung.", "Npgsql, Pomelo (MySQL), SQLite, Cosmos"],
    ], [1.1, 2.4, 1.6])
S.append(p(
    "Dieses Buch behandelt <b>Stufe 3</b> vollständig. Stufen 1 und 2 brauchen kein eigenes "
    "Buch: Sie nutzen nur die öffentliche API von EF, und Kapitel 7 behandelt, was an "
    "Fluent-API-Erweiterungen speziell ist. Die Werkzeuge von Stufe 4 (etwa der Austausch "
    "des SQL-Generators) werden dort erklärt, wo Plugins sie brauchen."))
S += h2("Für wen dieses Buch ist")
S.append(p(
    "Für Entwickler, die EF Core bereits sicher einsetzen: " + c("DbContext") + ", "
    "Migrationen, LINQ-Abfragen und Dependency Injection werden vorausgesetzt. Kenntnisse "
    "der Interna von EF sind nicht nötig; das Buch führt sie dort ein, wo sie gebraucht "
    "werden. Erfahrung mit PostgreSQL hilft beim Beispiel, aber jedes SQL-Konstrukt wird "
    "beim ersten Auftreten erklärt. Kapitel 2 endet mit einem vollständigen Mini-Plugin, "
    "das alle Bausteine auf einer Seite zeigt, bevor das große Beispiel beginnt."))
S += h2("Was ein Plugin der Stufe 3 leisten kann")
S.append(p(
    "Ein vollständiges Plugin kann EF Core an praktisch jeder Stelle erweitern: Es kann "
    "neue CLR-Typen als Spaltentypen verfügbar machen, eigene Funktionen in LINQ nach SQL "
    "übersetzen, das Modell automatisch per Convention anpassen, Metadaten über die Fluent "
    "API und Attribute annehmen, das Modell validieren, beim Speichern und beim Öffnen von "
    "Verbindungen eingreifen, eigene Ereignisse protokollieren, Migrationen um eigenes SQL "
    "ergänzen und die Werkzeuge von " + c("dotnet ef") + " erweitern. Das Beispiel in "
    "diesem Buch nutzt jede dieser Möglichkeiten."))
S += callout("tipp", [
    "Bevor man selbst schreibt, lohnt der Blick in bestehende Plugins. "
    "<b>EFCore.NamingConventions</b> ist ein kleines, sauberes Convention-Plugin. Das "
    "<b>NetTopologySuite</b>-Plugin im EF-Core-Repository zeigt Type Mappings und "
    "Übersetzungen. Das <b>NodaTime</b>-Plugin von Npgsql zeigt ein provider-spezifisches "
    "Plugin, das sich über den Options-Builder des Providers aktivieren lässt.",
], title="Vorbilder im Ökosystem")

# ------------------------------------------------------------ 2
S += h1("Die Architektur von EF Core aus Plugin-Sicht")
S += h2("Zwei Container: der interne Service Provider")
S.append(p(
    "Jeder " + c("DbContext") + " löst seine Infrastruktur aus einem <b>internen</b> "
    + c("IServiceProvider") + " auf. Er ist vom DI-Container der Anwendung getrennt und "
    "enthält mehrere hundert Dienste: Type Mapping, Model Building, Query-Pipeline, "
    "Change Tracking, Update-Pipeline, Migrations. Ein Plugin klinkt sich ein, indem es "
    "in diesen Container zusätzliche Dienste registriert oder vorhandene ersetzt."))
S += flow_diagram([
    ("DbContextOptions", "Provider-Extension (UseNpgsql) und Plugin-Extensions (UseAcmeTenancy)"),
    ("ServiceProviderCache", "Sucht per Hash einen passenden internen Provider oder baut einen"),
    ("ApplyServices()", "Jede Extension registriert ihre Dienste im neuen Provider"),
    ("Interner Service Provider", "Gecacht und von allen passenden Contexts geteilt"),
    ("Model (gecacht)", "Einmal pro Provider gebaut: Conventions, Type Mappings, Validierung"),
    ("Query, SaveChanges, Migrations", "Laufen mit den Diensten dieses Providers"),
])
S.append(p(
    "Zwei Caches sind dabei entscheidend. Der <b>Service Provider</b> wird anhand der Options "
    "gecacht: Contexts mit gleicher Konfiguration teilen sich einen Provider. Das "
    "<b>Modell</b> wird pro Provider (und Context-Typ) gecacht. Beides macht EF Core schnell, "
    "hat aber direkte Folgen für Plugins, die Kapitel 4 ausführlich behandelt."))

S += h2("Lebensdauern")
S.append(p(
    "Jeder EF-Dienst hat eine feste Lebensdauer. <b>Singleton</b> heißt hier: einmal pro "
    "internem Service Provider, nicht pro Anwendung. <b>Scoped</b> heißt: einmal pro "
    "Context-Instanz. Ein Singleton darf keinen Scoped-Dienst halten, und ein Dienst, der "
    "pro Context unterschiedliche Daten braucht, darf kein Singleton sein. Anhang A listet "
    "die Lebensdauern aller Erweiterungspunkte."))

S += h2("Das Dependencies-Muster")
S.append(p(
    "EF-Dienste bekommen ihre Abhängigkeiten nicht einzeln, sondern als ein "
    "Parameterobjekt, etwa " + c("MigrationsSqlGeneratorDependencies") + " oder " +
    c("ProviderConventionSetBuilderDependencies") + ". Wer von einem EF-Dienst erbt, "
    "übernimmt dieses Objekt im Konstruktor und reicht es weiter. Neue Abhängigkeiten "
    "fügt EF Core dem Objekt hinzu, ohne Konstruktoren zu brechen. Für Plugins ist das ein "
    "Segen: Abgeleitete Dienste bleiben über Minor-Versionen kompatibel."))
S += code_block('''
public class TenancyMigrationsSqlGenerator : NpgsqlMigrationsSqlGenerator
{
    public TenancyMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,     // Parameterobjekt von EF
        INpgsqlSingletonOptions npgsqlSingletonOptions)      // zusätzlich vom Provider
        : base(dependencies, npgsqlSingletonOptions)
    {
    }
''', label="Migrations/Internal/TenancyMigrationsSqlGenerator.cs (Kopf)")

S += callout("hinweis", [
    "Alle Klassen im Beispiel verwenden klassische Konstruktoren mit " +
    c("private readonly") + "-Feldern und " + c("ArgumentNullException.ThrowIfNull") +
    ", keine Primary Constructors. Deren Parameter sind im ganzen Klassenkörper veränderbar, "
    "bieten keinen natürlichen Ort für Validierung und verbergen, welcher Zustand gespeichert "
    "wird. Klassische Konstruktoren entsprechen außerdem dem Stil von EF Core und Npgsql, "
    "von deren Klassen ein Plugin erbt. Einzige Ausnahme sind positionelle Records wie " +
    c("RlsPolicy") + ": Dort erzeugt die Syntax echte " + c("init") + "-Properties.",
], title="Code-Stil im Beispiel")

S += h2("Erweitern oder ersetzen?")
S.append(p(
    "Für Erweiterungen gibt es zwei grundverschiedene Wege. <b>Plugin-Interfaces</b> wie " +
    c("IMethodCallTranslatorPlugin") + " oder " + c("IConventionSetPlugin") + " sind "
    "ausdrücklich dafür gedacht. Sie sind mehrfach registrierbar, EF fragt alle "
    "Registrierungen ab, und mehrere Plugins vertragen sich. <b>Ersetzen</b> bedeutet, "
    "einen Einzeldienst wie " + c("IMigrationsSqlGenerator") + " durch eine abgeleitete "
    "Klasse auszutauschen. Das ist manchmal unvermeidlich, aber es kann pro Context immer "
    "nur einer gewinnen."))
S += callout("wichtig", [
    "Immer zuerst prüfen, ob es ein Plugin-Interface gibt. Wer einen Einzeldienst ersetzt, "
    "kollidiert mit jedem anderen Plugin, das denselben Dienst ersetzt, und muss von der "
    "Provider-Implementierung erben. Das bindet das Plugin an einen Provider und an dessen "
    "interne API. Kapitel 5 zeigt, wie man trotzdem kombinierbar bleibt.",
], title="Plugin-Interfaces vor Ersetzen")

S += h2("Zum Aufwärmen: ein vollständiges Plugin in fünf Teilen")
S.append(p(
    "Vor dem großen Beispiel kommt hier das kleinste Plugin, das trotzdem vollständig ist. "
    "Es macht PostgreSQLs " + c("reverse()") + " in LINQ als " +
    c("EF.Functions.Reverse(...)") + " verfügbar. Jedes spätere Kapitel vertieft einen "
    "seiner fünf Teile:"))
S += code_block('''
// 1. Die Funktion, die Anwender in LINQ aufrufen; sie wird nie ausgeführt
public static class ReverseDbFunctionsExtensions
{
    public static string Reverse(this DbFunctions _, string value)
        => throw new InvalidOperationException("Reverse can only be used in LINQ queries.");
}

// 2. Der Translator macht aus dem Methodenaufruf eine SQL-Funktion (Kapitel 12)
public class ReverseTranslator : IMethodCallTranslator
{
    // Konstruktor speichert die ISqlExpressionFactory in _sql

    public SqlExpression? Translate(
        SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        => method == ReverseMethod
            ? _sql.Function("reverse", [arguments[1]], nullable: true,
                argumentsPropagateNullability: [true], typeof(string),
                arguments[1].TypeMapping)
            : null;
}

// 3. Das Plugin übergibt den Translator an EF (Kapitel 12)
public class ReverseTranslatorPlugin : IMethodCallTranslatorPlugin
{
    public ReverseTranslatorPlugin(ISqlExpressionFactory sql)
    {
        Translators = [new ReverseTranslator(sql)];
    }

    public IEnumerable<IMethodCallTranslator> Translators { get; }
}
''', label="tests/Acme.EntityFrameworkCore.Tenancy.Tests/MinimalPluginTests.cs (1/2, gekürzt)")
S += code_block('''
// 4. Die Options-Extension registriert das Plugin im internen Provider (Kap. 4, 5)
public class ReverseOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
        => new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IMethodCallTranslatorPlugin, ReverseTranslatorPlugin>();

    public void Validate(IDbContextOptions options)
    {
    }

    // ExtensionInfo: kein Provider, keine Settings -> Hash 0, immer derselbe Provider
}

// 5. Die Use-Methode aktiviert das Plugin (Kapitel 6)
public static DbContextOptionsBuilder UseReverse(this DbContextOptionsBuilder builder)
{
    ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
        builder.Options.FindExtension<ReverseOptionsExtension>()
        ?? new ReverseOptionsExtension());
    return builder;
}
''', label="tests/Acme.EntityFrameworkCore.Tenancy.Tests/MinimalPluginTests.cs (2/2, gekürzt)")
S += code_block('''
options.UseNpgsql(connectionString).UseReverse();

db.Customers.Where(c => EF.Functions.Reverse(c.Name) == "abc");
// SQL: WHERE reverse(c."Name") = 'abc'
''', label="Anwendung")
S.append(p(
    "Mehr braucht ein funktionierendes Plugin nicht. EF findet den Translator, weil die "
    "Extension ihn im internen Service Provider registriert, und alle Contexts, die mit " +
    c("UseReverse()") + " konfiguriert sind, teilen sich diesen Provider. Weil das Argument "
    "auf eine Spalte verweist, braucht die Funktion keinen Evaluatable-Expression-Filter "
    "(Kapitel 13 erklärt, wann sie einen braucht). Der Test " + c("MinimalPluginTests") +
    " prüft die Übersetzung und dass dieselbe Abfrage ohne das Plugin fehlschlägt. Das "
    "Mandanten-Plugin besteht aus genau diesen Teilen, nur aus sehr viel mehr davon."))

# ------------------------------------------------------------ 3
S += h1("Das Beispielszenario")
S.append(p(
    "Das Beispiel-Plugin <b>Acme.EntityFrameworkCore.Tenancy</b> macht EF Core mit "
    "PostgreSQL mandantenfähig. Die Trennung der Mandanten erzwingt die Datenbank selbst "
    "über <b>Row Level Security</b> (RLS): Jede Tabelle bekommt eine Policy, die nur Zeilen "
    "des Mandanten zeigt, dessen ID in der aktuellen Datenbank-Session gesetzt ist. Selbst "
    "eine fehlerhafte Abfrage oder rohes SQL kann so keine fremden Daten lesen."))
S.append(p("Aus Sicht des Anwenders sieht das so aus:"))
S += code_block('''
public class Order : ITenantOwned               // Marker aus dem Kernpaket
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }      // stark typisiert, automatisch uuid
    public decimal Total { get; set; }
}

services.AddAcmeTenancy<HttpTenantProvider>();
services.AddDbContext<ShopContext>((sp, o) => o
    .UseNpgsql(connectionString)
    .UseAcmeTenancy(sp, tenancy => tenancy.ApplyQueryFilter()));

// Abfragen und Speichern ohne jede Mandantenlogik im Anwendungscode
db.Orders.Add(new Order { Total = 42 });         // TenantId setzt das Plugin
var mine = await db.Orders.ToListAsync();        // RLS filtert in der Datenbank
''', label="Anwendersicht")
S.append(p("Damit das funktioniert, nutzt das Plugin jeden Erweiterungspunkt dieses Buchs:"))
S += data_table(
    ["Anforderung", "Umsetzung", "Kap."],
    [
        ["Konfiguration über " + c("UseAcmeTenancy()"),
         "Options-Extension, Settings-Record, Options-Builder", "4–6"],
        [c("TenantId") + " als Spaltentyp ohne Konfiguration",
         "Type-Mapping-Plugin, Value Converter, JSON-Reader/Writer", "10"],
        [c("EF.Functions.CurrentTenant()") + " in LINQ",
         "Method-Translator, Evaluatable-Expression-Filter", "12–13"],
        [c("tenantId.IsEmpty") + " in LINQ", "Member-Translator", "12"],
        ["RLS für alle " + c("ITenantOwned") + "-Entitäten",
         "Convention mit Index und optionalem Query-Filter", "8"],
        ["Fluent API, Attribut " + c("[NoTenantIsolation]"),
         "Annotations, Attribut-Convention, ConfigurationSource", "7–8"],
        ["Fehlkonfiguration früh erkennen", "Validierungs-Convention, eigene Warnungen", "9, 16"],
        ["TenantId automatisch setzen", "SaveChanges-Interceptor", "15"],
        ["Mandant in der DB-Session", "Connection-Interceptor", "15"],
        [c("ENABLE ROW LEVEL SECURITY") + ", " + c("CREATE POLICY"),
         "Annotation-Provider, Migrations-SQL-Generator", "17–18"],
        ["Rollen und Rechte in Migrationen", "Eigene Migrations-Operationen", "19"],
        ["Lesbare Snapshots", "Annotation-Code-Generator, Design-Time-Services", "20"],
        ["Compiled Models und AOT", c("Default") + "-Instanzen, Source-Generatoren", "21"],
        [c("EF.Functions.IsNotDistinctFrom") + ", " + c("EF.Functions.Every"),
         "Eigener SQL-Knoten, Aggregat-Translator", "37–38"],
        ["Mandanten-Property mit beliebigem Namen", c("IsTenantProperty()") + ", "
         "Laufzeit-Annotation", "39"],
        ["RLS aus bestehender Datenbank scaffolden", "Eigene Database-Model-Factory", "40"],
        ["SQL Server", "Zweites Provider-Paket auf demselben Kern", "41–42"],
    ], [1.6, 2, 0.4])

S += h2("Paketarchitektur")
S.append(p(
    "Das Plugin besteht aus drei NuGet-Paketen. Das <b>Kernpaket</b> enthält alles, was "
    "unabhängig von der Datenbank ist und was auch Assemblies mit Entitätsklassen "
    "referenzieren dürfen, ohne einen Provider mitzuziehen. Das <b>PostgreSQL-Paket</b> "
    "ist die vollständige Implementierung, an der dieses Buch alles erklärt. Das "
    "<b>SQL-Server-Paket</b> nutzt denselben Kern und zeigt in Kapitel 42, was ein zweiter "
    "Provider selbst umsetzen muss."))
S += code_block('''
Acme.EntityFrameworkCore.Tenancy               Kern (EF Core relational, ohne Provider)
├─ TenantId, TenantIdConverter, TenantIdJsonReaderWriter, TenancyException
├─ ITenantOwned, ITenantProvider, NoTenantIsolationAttribute
├─ EF.Functions.CurrentTenant(), TenancyDbFunctionsAttribute
├─ Metadata/         Laufzeit-Annotation, IsTenantProperty(), Kandidatensuche
├─ Conventions/      Value Generation für TenantId-Schlüssel
├─ Query/            Evaluatable-Expression-Filter (für alle Pakete)
├─ Interceptors/     SaveChanges-Interceptor, abstrakter Session-Interceptor
├─ Diagnostics/      Event-IDs, Logging-Definitionen, Logger-Extensions
└─ DependencyInjection/  AddAcmeTenancy<T>()

Acme.EntityFrameworkCore.Tenancy.PostgreSQL    vollständige Implementierung
├─ Infrastructure/   Options-Extension, Builder, Singleton-Options
├─ Storage/          TenantIdTypeMapping, Type-Mapping-Plugin
├─ Query/            Translators, eigener SQL-Knoten, Aggregat, SQL-Generator
├─ Metadata/         RLS-Annotations, RlsPolicy, Annotation-Provider
├─ Builders/         Fluent API (HasRowLevelSecurity, HasPolicy, ...)
├─ Conventions/      Isolation, Attribut, Validierung, Convention-Set-Plugin
├─ Migrations/       SQL-Generator, eigene Operationen
├─ Design/           Code-Generatoren, Design-Time-Services
├─ Scaffolding/      RLS und Policies aus der Datenbank lesen
└─ Runtime/          TenantConnectionInterceptor

Acme.EntityFrameworkCore.Tenancy.SqlServer     zweiter Provider (Kapitel 42)
└─ Infrastructure/, Storage/, Query/, Metadata/, Conventions/, Migrations/, Runtime/
''', lang="text", label="Struktur der drei Pakete")
S += callout("tipp", [
    "Die Ordnerstruktur folgt der von EF Core selbst (Infrastructure, Storage, Query, "
    "Metadata, Migrations, Design). Wer EF Core kennt, findet sich sofort zurecht, und "
    "beim Nachschlagen im EF-Quellcode weiß man, wo das Gegenstück liegt.",
])
