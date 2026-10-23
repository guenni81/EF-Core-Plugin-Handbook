# ============================================================ ANHÄNGE
S += part(
    "Anhänge",
    "Nachschlagen: Erweiterungspunkte mit Lebensdauern, Dateien des Beispiels, Begriffe und "
    "ein Debugging-Werkzeugkasten.",
    ["Anhang A: Erweiterungspunkte und Lebensdauern",
     "Anhang B: Dateien des Beispiel-Plugins",
     "Anhang C: Glossar",
     "Anhang D: Debugging-Werkzeugkasten",
     "Lizenz"])

S += h1("Anhang A: Erweiterungspunkte und Lebensdauern", numbered=False)
S.append(p(
    "Lebensdauern und Kardinalität laut den Service-Tabellen von EF Core 10 (" +
    c("EntityFrameworkServicesBuilder") + ", " + c("EntityFrameworkRelationalServicesBuilder") +
    "). „Mehrfach“ heißt: Plugin-Interface, alle Registrierungen werden verwendet."))
S += data_table(
    ["Dienst", "Lebensdauer", "Art", "Kap."],
    [
        [c("IConventionSetPlugin"), "Scoped", "mehrfach", "8"],
        [c("ISingletonOptions"), "Singleton", "mehrfach", "5"],
        [c("ITypeMappingSourcePlugin"), "Singleton", "mehrfach", "10"],
        [c("IRelationalTypeMappingSourcePlugin"), "Singleton", "mehrfach", "10"],
        [c("IMethodCallTranslatorPlugin"), "Scoped", "mehrfach", "12"],
        [c("IMemberTranslatorPlugin"), "Scoped", "mehrfach", "12"],
        [c("IAggregateMethodCallTranslatorPlugin"), "Scoped", "mehrfach", "38"],
        [c("IEvaluatableExpressionFilterPlugin"), "Singleton", "mehrfach", "13"],
        [c("IInterceptor") + " (injiziert)", "Scoped", "mehrfach", "15"],
        [c("ISingletonInterceptor"), "Singleton", "mehrfach", "15"],
        [c("IRelationalAnnotationProvider"), "Singleton", "einzeln", "18"],
        [c("IMigrationsSqlGenerator"), "Scoped", "einzeln", "18"],
        [c("IMigrationsModelDiffer"), "Scoped", "einzeln", "19"],
        [c("IModelValidator"), "Singleton", "einzeln", "9"],
        [c("IModelCustomizer"), "Singleton", "einzeln", "–"],
        [c("IModelCacheKeyFactory"), "Singleton", "einzeln", "31"],
        [c("ISqlGenerationHelper"), "Singleton", "einzeln", "–"],
        [c("IRelationalTypeMappingSource"), "Singleton", "einzeln", "10"],
        [c("ISqlExpressionFactory"), "Scoped", "einzeln", "12"],
        [c("IQuerySqlGeneratorFactory"), "Singleton", "einzeln", "14, 37"],
        [c("IModificationCommandFactory"), "Singleton", "einzeln", "29"],
        [c("ICommandBatchPreparer"), "Scoped", "einzeln", "29"],
        [c("IModificationCommandBatchFactory"), "Scoped", "einzeln", "29"],
        [c("IUpdateSqlGenerator"), "Singleton", "einzeln", "29"],
        [c("IRelationalParameterBasedSqlProcessorFactory"), "Scoped", "einzeln", "37"],
    ], [2.4, 0.9, 0.8, 0.4])
S += h2("Design-Time-Dienste (über IDesignTimeServices)")
S += data_table(
    ["Dienst", "Zweck"],
    [
        [c("IAnnotationCodeGenerator"), "Annotations als Fluent API in Snapshot und Scaffolding"],
        [c("ICSharpRuntimeAnnotationCodeGenerator"), "Annotations im Compiled Model filtern "
         "oder komplexe Werte übersetzen (Kapitel 39)"],
        [c("ICSharpMigrationOperationGenerator"), "Eigene Operationen als C# in Migrationen"],
        [c("IDatabaseModelFactory"), "Schema lesen für Scaffolding (Kapitel 40)"],
        [c("IProviderConfigurationCodeGenerator"), "UseXyz-Aufruf im gescaffoldeten Context"],
    ], [2, 2.6])

S += h1("Anhang B: Dateien des Beispiel-Plugins", numbered=False)
S += data_table(
    ["Datei", "Inhalt", "Kap."],
    [
        ["<b>Kernpaket</b>", "", ""],
        [c("TenantId.cs") + ", " + c("TenantIdConverter.cs"), "Stark typisierte ID, Konverter", "10"],
        [c("TenantIdJsonReaderWriter.cs"), "JSON-Unterstützung", "10"],
        [c("ITenantOwned.cs") + ", " + c("ITenantProvider.cs"), "Marker, Mandantenquelle", "3"],
        [c("NoTenantIsolationAttribute.cs"), "Data Annotation", "8"],
        [c("TenancyDbFunctionsExtensions.cs"), c("EF.Functions.CurrentTenant()"), "12"],
        [c("Diagnostics/*"), "Event-IDs, Definitionen, Logger-Extensions", "16"],
        [c("Interceptors/TenantSaveChangesInterceptor.cs"), "SaveChanges-Logik", "15"],
        [c("Interceptors/TenantSessionInterceptor.cs"), "Abstrakte Basis für Session-Befehle",
         "15, 41"],
        [c("TenancyDatabaseFacadeExtensions.cs"), c("Database.ApplyTenantSession()"), "15"],
        [c("Metadata/*") + " (Kern)", "Laufzeit-Annotation, IsTenantProperty, Kandidatensuche", "39"],
        [c("Query/TenancyEvaluatableExpressionFilterPlugin.cs"), "Filter für alle Pakete", "13, 41"],
        [c("DependencyInjection/*"), "AddAcmeTenancy", "6, 41"],
        [c("ValueGeneration/TenantIdValueGenerator.cs") + ", " + c("…Factory.cs"), "Wertgenerierung", "28"],
        [c("Conventions/TenantIdKeyValueGenerationConvention.cs"), "Generator für Schlüssel", "28"],
        ["<b>PostgreSQL-Paket</b>", "", ""],
        [c("Infrastructure/TenancyOptionsExtension.cs"), "Extension, Info, ApplyServices", "4, 5"],
        [c("Infrastructure/TenancySettings.cs"), "Einstellungen als Record", "4"],
        [c("Infrastructure/ITenancySingletonOptions.cs"), "Singleton-Options", "5"],
        [c("Infrastructure/TenancyOptionsBuilder.cs"), "Options-Builder", "6"],
        [c("Infrastructure/Tenancy*Extensions.cs"), "UseAcmeTenancy, AddAcmeTenancy", "6"],
        [c("Infrastructure/TenancyIdentifiers.cs"), "Validierung von Bezeichnern", "8, 19"],
        [c("Storage/*"), "Type Mapping und Plugin", "10"],
        [c("Query/*"), "Translators, Evaluatable-Filter", "12, 13"],
        [c("Metadata/*"), "Annotations, RlsPolicy, PolicyCommand, Annotation-Provider", "7, 18"],
        [c("Builders/*"), "Fluent API, PolicyBuilder", "7"],
        [c("Conventions/*"), "Isolation, Attribut, Validierung, Plugin", "8, 9"],
        [c("Migrations/*"), "SQL-Generator, eigene Operationen", "18, 19"],
        [c("Design/*") + ", " + c("Properties/AssemblyInfo.cs"), "Code-Generator, Design-Time", "20"],
        [c("Runtime/TenantConnectionInterceptor.cs"), "Session-Setting setzen", "15"],
        [c("Query/Expressions/*") + ", " + c("Query/DistinctFromTranslator.cs"),
         "Eigener SQL-Knoten", "37"],
        [c("Query/Internal/*"), "SQL-Generator, Nullability-Prozessor, Factories", "37"],
        [c("Query/EveryAggregateTranslator.cs"), "Aggregat-Translator", "38"],
        [c("Design/Internal/TenancyCSharpRuntime…Generator.cs"), "Compiled-Model-Filter", "39"],
        [c("Scaffolding/*"), "RLS aus der Datenbank lesen", "40"],
        ["<b>SQL-Server-Paket</b>", "", ""],
        [c("Infrastructure/*"), "Extension, Provider-Builder-Aktivierung", "42"],
        [c("Storage/*") + ", " + c("Query/*"), "uniqueidentifier, SESSION_CONTEXT", "42"],
        [c("Conventions/*") + ", " + c("Metadata/*"), "Isolation, Spalten-Annotation", "42"],
        [c("Migrations/*"), "Predicate-Funktion, Security Policy", "42"],
        [c("Runtime/*"), "sp_set_session_context", "42"],
        ["<b>Weitere</b>", "", ""],
        [c("samples/ShopSample"), "Konsolen-Beispiel mit Design-Time-Factory", "3, 32"],
        [c("samples/ShopApi"), "ASP.NET-Core-Beispiel (Minimal API)", "35"],
        [c(".github/workflows/ci.yml"), "Build, Tests, Handbuch, Preview-Build", "36"],
        [c("PublicAPI.*.txt"), "Public-API-Analyzer", "36"],
        [c("docs/handbook/*"), "Quellen dieses Buchs (Deutsch und Englisch)", "–"],
        [c("tests/...Tests"), "Unit-Tests ohne Datenbank", "23"],
        [c("tests/.../MinimalPluginTests.cs"), "Das Mini-Plugin", "2"],
        [c("tests/.../CompositionTests.cs"), "Decorator-Beispiel (Audit-Plugin)", "5"],
        [c("tests/.../DebuggingTests.cs"), "Debugging-Techniken", "Anh. D"],
        [c("tests/.../SchemaPerTenantTests.cs"), "Modell-Cache-Schlüssel pro Schema", "31"],
        [c("benchmarks/..."), "BenchmarkDotNet-Projekt für die SaveChanges-Logik", "34"],
        [c("tests/...IntegrationTests"), "Testcontainers-Tests gegen PostgreSQL", "15, 23, 27, 33, 40"],
        [c("tests/...SqlServer.Tests"), "Unit- und Testcontainers-Tests für SQL Server", "42"],
    ], [2.3, 2, 0.5])

S += h1("Anhang C: Glossar", numbered=False)
S += data_table(
    ["Begriff", "Erklärung"],
    [
        ["Annotation", "Schlüssel-Wert-Metadatum an einem Modellelement."],
        ["ConfigurationSource", "Herkunft eines Metadatums (Explicit, DataAnnotation, "
         "Convention); bestimmt, wer wen überschreiben darf."],
        ["Convention", "Regel, die das Modell beim Bau automatisch anpasst."],
        ["Decorator", "Dienst, der die zuvor registrierte Implementierung umhüllt und an sie "
         "delegiert; verträgt sich mit anderen Plugins (Kapitel 5)."],
        ["Design-Time-Modell", "Vollständiges Modell mit allen Annotations; Grundlage für "
         "Migrations. Gegenstück: schlankes Laufzeitmodell."],
        ["Differ", c("IMigrationsModelDiffer") + "; vergleicht zwei relationale Modelle."],
        ["Funcletizer", "Erste Query-Stufe; wertet Ausdrücke ohne Datenbezug in .NET aus."],
        ["Nullability-Prozessor", "Query-Stufe, die NULL-Semantik auswertet und Vergleiche "
         "optimiert; muss eigene SQL-Knoten kennen."],
        ["SqlExpression", "Knoten im SQL-Ausdrucksbaum; eigene Knoten für Syntax, die keine "
         "Funktion ist."],
        ["Laufzeit-Annotation", "Annotation, die zur Laufzeit gelesen wird und deshalb im "
         "Laufzeit- und Compiled Model erhalten bleiben muss."],
        ["Interner Service Provider", "EFs eigener DI-Container pro Konfiguration."],
        ["Pubternal", "Öffentlich sichtbare, aber interne API ohne Kompatibilitätsgarantie (EF1001)."],
        ["Relationales Modell", "Tabellen, Spalten, Indizes, abgeleitet aus dem EF-Modell."],
        ["RLS", "Row Level Security: Zeilenfilter, die PostgreSQL selbst erzwingt."],
        ["Snapshot", "Generierter C#-Code mit dem Modellstand der letzten Migration."],
        ["Type Mapping", "Verbindung zwischen CLR-Typ und Datenbanktyp inklusive "
         "Konvertierung, Vergleich und Literalen."],
    ], [1.3, 3])

S += h1("Anhang D: Debugging-Werkzeugkasten", numbered=False)
S.append(p(
    "Wenn sich ein Plugin nicht wie erwartet verhält, lautet die Frage fast immer eine von "
    "fünf. Jede der folgenden Techniken wird in " + c("DebuggingTests") + " geprüft."))
S += data_table(
    ["Frage", "Technik"],
    [
        ["Ist meine Annotation im Modell?", c("ToDebugString(MetadataDebugStringOptions"
         ".LongDefault)") + " auf dem Design-Time-Modell"],
        ["Welche Implementierung eines Einzeldienstes hat gewonnen?",
         c("context.GetService<IMigrationsSqlGenerator>().GetType()")],
        ["Ist meine Convention registriert?", c("GetService<IConventionSetBuilder>()"
         ".CreateConventionSet()") + ", dann z. B. " + c("ModelFinalizingConventions")],
        ["Wie sieht die Abfrage vor und nach der Übersetzung aus?",
         c("query.Expression.Print()") + " und " + c("query.ToQueryString()")],
        ["Protokolliert mein Plugin?", c("LogTo(lines.Add, [eventId], LogLevel.Debug)") +
         ", beschränkt auf die Event-IDs des Plugins"],
    ], [1.4, 3.2])
S += code_block('''
using var ctx = CreateContext();

// 1. Modell: Annotations, Ergebnisse von Conventions, Type Mappings
var model = ctx.GetService<IDesignTimeModel>().Model;
Console.WriteLine(model.ToDebugString(MetadataDebugStringOptions.LongDefault));

// 2. Dienste: welcher Ersatz hat gewonnen?
Console.WriteLine(ctx.GetService<IMigrationsSqlGenerator>().GetType());

// 3. Conventions: ist meine in der Liste?
var conventions = ctx.GetService<IConventionSetBuilder>().CreateConventionSet();
Console.WriteLine(string.Join("\\n", conventions.ModelFinalizingConventions));

// 4. Abfragen: LINQ-Baum vor, SQL nach der Übersetzung
var query = ctx.Orders.Where(o => o.TenantId == EF.Functions.CurrentTenant());
Console.WriteLine(query.Expression.Print());
Console.WriteLine(query.ToQueryString());
''', label="Debugging-Snippets (geprüft in tests/.../DebuggingTests.cs)")
S += h2("Weitere Werkzeuge")
S += data_table(
    ["Werkzeug", "Einsatz"],
    [
        ["Debugger mit Source Link, „Just My Code“ aus", "In EF Core und Npgsql "
         "hineinsteppen; beide liefern Symbole und Source Link mit. Ein Breakpoint in der "
         "eigenen Convention zeigt, wann und wie oft sie läuft."],
        [c("DebugView") + " im Debugger", "Modell, Entitätstypen, Query-Objekte und der "
         "Change Tracker zeigen dieselben Texte wie " + c("ToDebugString") + "."],
        [c("dotnet ef ... --verbose"), "Zeigt, welche Assemblies und Design-Time-Dienste die "
         "Tools laden; die erste Anlaufstelle, wenn ein Design-Time-Dienst ignoriert zu "
         "werden scheint."],
        [c("EnableSensitiveDataLogging") + ", " + c("EnableDetailedErrors"), "Parameterwerte "
         "und ausführliche Fehler im Log; nur für die Entwicklung."],
        [c("ManyServiceProvidersCreatedWarning"), "EF warnt, wenn zu viele interne Provider "
         "entstehen: fast immer ein Hash-Fehler (Kapitel 4)."],
        ["Der Quellcode von EF Core auf GitHub", "Die maßgebliche Dokumentation interner "
         "APIs; die Ordnerstruktur entspricht den Namespaces."],
    ], [1.4, 3.2])
S += callout("tipp", [
    "Fast jedes Plugin-Problem lässt sich ohne Datenbank reproduzieren: einen Context mit "
    "dem Plugin bauen und das Modell, die aufgelösten Dienste oder " +
    c("ToQueryString()") + " ansehen. Ein solcher Test iteriert schneller als die Anwendung "
    "und bleibt danach als Regressionstest erhalten.",
], title="Im Test reproduzieren, nicht in der App")

# ------------------------------------------------------------ Lizenz
S += h1("Lizenz", numbered=False)
S.append(p(
    "Dieses Handbuch und der Beispielcode im Repository stehen unter der MIT-Lizenz. Sie "
    "dürfen beides nutzen, kopieren, verändern und weitergeben, auch kommerziell, sofern "
    "der folgende Copyright- und Lizenzhinweis erhalten bleibt. Maßgeblich ist der "
    "englische Originalwortlaut:"))
MIT_TEXT = [
    "Copyright (c) 2026 guenni81",
    "Permission is hereby granted, free of charge, to any person obtaining a copy of this "
    "software and associated documentation files (the \u201cSoftware\u201d), to deal in the "
    "Software without restriction, including without limitation the rights to use, copy, "
    "modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, "
    "and to permit persons to whom the Software is furnished to do so, subject to the "
    "following conditions:",
    "The above copyright notice and this permission notice shall be included in all copies "
    "or substantial portions of the Software.",
    "THE SOFTWARE IS PROVIDED \u201cAS IS\u201d, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR "
    "IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A "
    "PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT "
    "HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF "
    "CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE "
    "OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.",
]
for paragraph in MIT_TEXT:
    S.append(p(paragraph))
S.append(p(
    "Die im PDF eingebetteten DejaVu-Schriften stehen unter einer eigenen freien Lizenz "
    "(Bitstream-Vera-Schriftlizenz; die DejaVu-Änderungen sind gemeinfrei); siehe " +
    c("docs/handbook/fonts/LICENSE-DejaVu.txt") + " im Repository."))
