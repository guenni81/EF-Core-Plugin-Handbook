# ============================================================ TEIL VII
S += part(
    "Qualität und Auslieferung",
    "Testen, versionieren, paketieren, und die Checkliste für den Weg von A bis Z.",
    ["Kapitel 23: Teststrategie",
     "Kapitel 24: Versionierung und Kompatibilität",
     "Kapitel 25: Packaging und Dokumentation",
     "Kapitel 26: Die Checkliste von A bis Z"])

# ------------------------------------------------------------ 23
S += h1("Teststrategie")
S.append(p(
    "Plugins lassen sich erstaunlich weit ohne Datenbank testen. Modellbau, Type Mappings, "
    "SQL-Übersetzung, Migrations-SQL und Snapshots brauchen nur einen Connection-String, "
    "keine Verbindung. Die Teststrategie des Beispiels hat vier Ebenen:"))
S += data_table(
    ["Ebene", "Prüft", "Datenbank"],
    [
        ["Modelltests", "Annotations, Conventions, Validierung, Optionen, Caching", "nein"],
        ["SQL-Tests", "Übersetzungen per " + c("ToQueryString") + ", Migrations-SQL per "
         "Differ und Generator", "nein"],
        ["Snapshot-Tests", "Code-Generator als String und als Roundtrip (Roslyn)", "nein"],
        ["Integrationstests", "Echtes Verhalten: RLS, Rollen, Interceptors (Testcontainers)",
         "ja (Docker)"],
    ], [1.2, 2.6, 0.8])

S += h2("Testhilfen")
S.append(p(
    "Tests bauen viele Contexts mit unterschiedlichem " + c("OnModelCreating") + ". Weil EF "
    "das Modell pro internem Provider cached, muss jeder Test einen eigenen Provider "
    "bekommen; dafür gibt es " + c("EnableServiceProviderCaching(false)") + ":"))
S += code_block('''
public static TestContext Create(
    Action<TenancyOptionsBuilder>? tenancy = null,
    Action<ModelBuilder>? model = null,
    bool cacheServiceProvider = false,
    Action<DbContextOptionsBuilder>? configure = null)
{
    var builder = new DbContextOptionsBuilder<TestContext>()
        .UseNpgsql("Host=localhost;Database=unused")       // keine Verbindung nötig
        .UseAcmeTenancy(tenancy)
        .EnableServiceProviderCaching(cacheServiceProvider);
    configure?.Invoke(builder);
    return new TestContext(builder.Options, model);
}

public static IReadOnlyList<string> MigrationSql(DbContext? source, DbContext target)
{
    var operations = target.GetService<IMigrationsModelDiffer>().GetDifferences(
        source?.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
        target.GetService<IDesignTimeModel>().Model.GetRelationalModel());

    return target.GetService<IMigrationsSqlGenerator>()
        .Generate(operations, target.GetService<IDesignTimeModel>().Model)
        .Select(c => c.CommandText.Trim()).ToList();
}
''', label="tests/.../TestModel.cs (gekürzt)")
S += callout("wichtig", [
    "Für Migrations-Tests immer das <b>Design-Time-Modell</b> verwenden (" +
    c("IDesignTimeModel") + "). " + c("context.Model") + " ist das schlanke Laufzeitmodell; "
    "Annotations, die der Annotation-Provider nur zur Design-Zeit weitergibt, fehlen darin.",
], title="Design-Time-Modell verwenden")
S += callout("hinweis", [
    "Tests gegen SQLite oder den InMemory-Provider sind verlockend, weil sie kein Docker "
    "brauchen. Für ein Plugin beweisen sie wenig: InMemory ist nicht relational, Type "
    "Mappings, Translators und Migrationen laufen also nie; SQLite hat einen anderen "
    "SQL-Dialekt, andere Type Mappings und keine Row Level Security. Ein "
    "provider-spezifisches Plugin wird mit seinem eigenen Provider getestet: ohne Datenbank "
    "(nur Connection-String) für alles bis zum SQL-Text, gegen die echte Datenbank für das "
    "Verhalten.",
], title="Warum nicht SQLite oder InMemory?")

S += h2("Was getestet wird")
S += data_table(
    ["Test", "Sichert ab"],
    [
        ["Caching mit verschiedenen Optionen", "Zwei Contexts mit verschiedenen Settings "
         "bekommen verschiedene Modelle (Hash-Fehler aus Kapitel 4)."],
        ["Ungültige Setting-Namen", "Validierung inklusive Injection-Versuch."],
        ["Type Mapping ohne Konfiguration", c("TenantId") + " wird " + c("uuid") +
         "; Literal-Format; anderer Spaltentyp wird nicht beansprucht."],
        [c("CurrentTenant()") + " per " + c("ToQueryString"), "Übersetzung und dass nichts "
         "clientseitig ausgewertet wird (Kapitel 13)."],
        ["Explizit schlägt Convention", "Eigene Policy gleichen Namens bleibt erhalten; "
         "Fluent API überstimmt das Attribut."],
        ["Warning-as-Error", "Opt-out-Warnung wird mit " + c("ConfigureWarnings") + " zum Fehler."],
        ["Validierung", "RLS am abgeleiteten Typ und ungültige Policies werfen beim Modellbau."],
        ["Keine Phantom-Migration", "Unverändertes Modell erzeugt keine einzige Operation."],
        ["Migrations-SQL", "Initial, Änderung (DROP + CREATE), Opt-out, eigene Operationen."],
        ["SaveChanges-Logik", "Setzen, fremder Mandant, Umhängen, kein Mandant."],
        ["Snapshot", "Fluent API statt Annotations; Roundtrip ohne Unterschiede."],
        ["RLS in PostgreSQL", "Isolation, keine Zeilen ohne Mandant, WITH CHECK greift."],
        ["Grenzfälle (Kapitel 27–32)", "ExecuteUpdate und Raw SQL gegen RLS, Value Generation, "
         "JSON-Spalten, Compiled Model, idempotentes Skript."],
        ["Kombinierbarkeit", "Decorator um den Migrations-Generator (Kapitel 5), "
         "EFCore.NamingConventions in beiden Reihenfolgen (Kapitel 8)."],
        ["Annotation-Format", "Serialisierte Policies bleiben identisch; tolerantes Lesen "
         "(Kapitel 24)."],
        ["Context-Lebensdauern", "DbContext-Factory, Hintergrundjobs, offene Verbindungen "
         "(Kapitel 15, 35)."],
        ["Mandantenübergreifender Zugriff", "Reporting-Rolle liest alle Mandanten, kann aber "
         "nicht schreiben; die App-Rolle kann RLS nicht abschalten (Kapitel 33)."],
    ], [1.5, 2.5])

S += h2("Der Snapshot-Roundtrip")
S.append(p(
    "Der wertvollste Test für Design-Time-Code: Snapshot generieren, mit Roslyn "
    "kompilieren, Modell daraus bauen und gegen das Original diffen. Jede Abweichung "
    "zwischen Code-Generator und Fluent API würde sonst bei jedem Nutzer eine "
    "Phantom-Migration erzeugen:"))
S += code_block('''
[Fact]
public void Snapshot_roundtrip_produces_no_differences()
{
    using var ctx = CreateContext();
    var snapshot = Compile(GenerateSnapshot(ctx));

    // Wie im EF-Migrator: finalisieren und initialisieren
    var model = snapshot.Model;
    if (model is IMutableModel mutable) model = mutable.FinalizeModel();
    model = ctx.GetService<IModelRuntimeInitializer>()
        .Initialize(model, designTime: true, validationLogger: null);

    var differences = ctx.GetService<IMigrationsModelDiffer>().GetDifferences(
        model.GetRelationalModel(), ctx.DesignModel().GetRelationalModel());

    Assert.Empty(differences);
}
''', label="tests/.../SnapshotTests.cs (Auszug)")

S += h2("Integrationstests mit Testcontainers")
S.append(p(
    "Das echte Sicherheitsverhalten lässt sich nur gegen PostgreSQL prüfen. Die Fixture "
    "startet einen Container, legt das Schema mit " + c("EnsureCreated") + " an (inklusive "
    "RLS, Kapitel 17), führt die eigenen Migrations-Operationen real aus und verbindet die "
    "Tests mit einer eingeschränkten Rolle:"))
S += code_block('''
await owner.Database.EnsureCreatedAsync();          // RLS, Policies, Index

var commands = owner.GetService<IMigrationsSqlGenerator>().Generate(
[
    new CreateApplicationRoleOperation { Name = AppRole, CanLogin = true },
    new GrantTablePrivilegesOperation { Role = AppRole },
]);
await owner.GetService<IMigrationCommandExecutor>().ExecuteNonQueryAsync(
    commands, owner.GetService<IRelationalConnection>(),
    new MigrationExecutionState(), commitTransaction: true);
''', label="tests/...IntegrationTests/PostgresFixture.cs (Auszug)")
S += code_block('''
[Fact]
public async Task Database_rejects_rows_of_other_tenant()
{
    // Interceptor umgehen: direktes SQL. WITH CHECK der Policy muss greifen.
    var tenants = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
    await using var db = _fixture.CreateAppContext(tenants);

    var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
        $"INSERT INTO \\"Orders\\" (\\"TenantId\\", \\"Total\\") VALUES ({Guid.NewGuid()}, 1)"));

    Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
}
''', label="tests/...IntegrationTests/RowLevelSecurityTests.cs (Auszug)")
S += callout("tipp", [
    "Für Provider und sehr tiefe Plugins stellt EF Core die <b>Specification Tests</b> "
    "bereit (" + c("Microsoft.EntityFrameworkCore.Relational.Specification.Tests") + "). "
    "Das ist dieselbe Testsuite, gegen die EF und die Provider selbst getestet werden. "
    "Das NetTopologySuite-Plugin im EF-Repository zeigt, wie ein Plugin sie nutzt.",
])

# ------------------------------------------------------------ 24
S += h1("Versionierung und Kompatibilität")
S += h2("Versionen an EF Core ausrichten")
S.append(p(
    "Die Major-Version des Plugins folgt der von EF Core: Version 10.x für EF Core 10. "
    "Nutzer erkennen sofort, welche Kombination passt, und das Plugin kann mit jeder "
    "EF-Major-Version Breaking Changes übernehmen. Die Abhängigkeiten werden auf diese "
    "Major-Version begrenzt:"))
S += code_block('''
<PackageVersion Include="Microsoft.EntityFrameworkCore" Version="[10.0.0,11.0.0)" />
<PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="[10.0.0,11.0.0)" />
''', lang="xml", label="Directory.Packages.props (Auszug)")

S += h2("Umgang mit interner API")
S.append(p(
    "Plugins kommen um interne APIs selten herum: Das Beispiel erbt von " +
    c("NpgsqlMigrationsSqlGenerator") + ", " + c("NpgsqlAnnotationProvider") + " und " +
    c("NpgsqlAnnotationCodeGenerator") + " und nutzt " + c("NpgsqlOptionsExtension") +
    ". Solche Typen können sich auch in Minor-Versionen ändern. EF meldet ihre Verwendung "
    "mit der Warnung <b>EF1001</b>."))
S += data_table(
    ["Maßnahme", "Wirkung"],
    [
        ["Öffentliche Alternative suchen", "Jede vermiedene interne API ist ein Risiko weniger."],
        ["Interne Zugriffe bündeln", "Wenige Klassen kapseln sie; ein Bruch betrifft eine Stelle."],
        [c("<NoWarn>EF1001</NoWarn>") + " bewusst setzen", "Projektweit und dokumentiert, nicht "
         "pro Zeile verstreut."],
        ["Versionsbereich begrenzen", "Verhindert, dass Nutzer ungetestete Kombinationen bekommen."],
        ["CI gegen Previews", "Ein Build gegen die nächste EF-Preview zeigt Brüche Monate früher."],
        ["Roundtrip- und SQL-Tests", "Bemerken Verhaltensänderungen, die der Compiler nicht sieht."],
    ], [1.6, 2.4])
S += callout("hinweis", [
    "Mehrere EF-Major-Versionen gleichzeitig zu unterstützen ist per Multi-Targeting möglich, "
    "verdoppelt aber Testaufwand und bedingte Kompilierung. Für die meisten Plugins ist ein "
    "Release-Zweig pro EF-Major-Version einfacher.",
], title="Multi-Targeting")

S += h2("Annotation-Formate weiterentwickeln")
S.append(p(
    "Alles, was ein Plugin in Annotations schreibt, landet in den Model-Snapshots seiner "
    "Anwender, oft lange vor dem nächsten Upgrade. Serialisiert eine neue Plugin-Version "
    "dieselbe Konfiguration anders (eine zusätzliche JSON-Property, eine andere "
    "Reihenfolge, eine andere Enum-Schreibweise), vergleicht der Differ zwei verschiedene "
    "Strings. Jeder Anwender bekommt dann eine Migration, die alle Policies ohne echte "
    "Änderung löscht und neu anlegt. Das serialisierte Format gehört deshalb genauso zur "
    "öffentlichen API wie die Methoden."))
S += data_table(
    ["Regel", "Wie"],
    [
        ["Format per Test festschreiben", "Ein Golden-Value-Test vergleicht die serialisierte "
         "Form Zeichen für Zeichen."],
        ["Tolerant lesen", "Fehlende Properties werden zu Defaults, unbekannte werden "
         "ignoriert (das Standardverhalten von System.Text.Json)."],
        ["Nur ergänzen, Defaults nicht schreiben", "Neue optionale Properties mit " +
         c("[JsonIgnore(Condition = WhenWritingDefault)]") + " lassen bestehende "
         "Annotations unverändert."],
        ["Echte Formatänderungen nur mit Major-Version", "Dokumentieren, am besten zusammen "
         "mit dem EF-Major-Upgrade, wenn Anwender ohnehin mit Migrationen rechnen."],
    ], [1.7, 2.6])
S += code_block('''
[Fact]
public void Serialized_policy_format_is_stable()
{
    var policy = new RlsPolicy("support_read", PolicyCommand.Select, Permissive: false,
        Roles: ["shop_support"], Using: "true", WithCheck: null);

    // Golden Value: eine Änderung bricht bestehende Snapshots
    Assert.Equal(
        """{"Name":"support_read","Command":"Select","Permissive":false"""
        + ""","Roles":["shop_support"],"Using":"true","WithCheck":null}""",
        policy.Serialize());
}
''', label="tests/.../AnnotationFormatTests.cs (Auszug)")
S.append(p(
    "Zwei weitere Tests prüfen das tolerante Lesen in beide Richtungen. Der "
    "Snapshot-Roundtrip aus Kapitel 23 ergänzt sie: Der Roundtrip zeigt, dass der aktuelle "
    "Code-Generator und die Fluent API übereinstimmen, der Golden Value zeigt, dass die "
    "nächste Version noch zu den Snapshots passt, die Anwender bereits haben."))

# ------------------------------------------------------------ 25
S += h1("Packaging und Dokumentation")
S.append(p(
    "Gemeinsame Einstellungen liegen in " + c("Directory.Build.props") + ", Paketversionen "
    "zentral in " + c("Directory.Packages.props") + " (Central Package Management):"))
S += code_block('''
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  <NoWarn>$(NoWarn);EF1001</NoWarn>
  <Deterministic>true</Deterministic>
</PropertyGroup>
<PropertyGroup>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageReadmeFile>README.md</PackageReadmeFile>
  <VersionPrefix>10.0.0</VersionPrefix>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>      <!-- SourceLink -->
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
</PropertyGroup>
''', lang="xml", label="Directory.Build.props (gekürzt)")
S += data_table(
    ["Element", "Warum"],
    [
        [c("GenerateDocumentationFile"), "XML-Doku im Paket: Nutzer sehen Erklärungen in der IDE."],
        [c("IsAotCompatible"), "Analyzer melden AOT- und Trimming-Probleme beim Build."],
        ["SourceLink, " + c(".snupkg"), "Nutzer können in den Plugin-Code debuggen. Seit dem "
         ".NET-8-SDK eingebaut."],
        ["README im Paket", "Erscheint auf nuget.org; Minimalbeispiel und Kompatibilität."],
        ["CHANGELOG", "Breaking Changes klar markieren, besonders bei EF-Major-Wechseln."],
        [c("InternalsVisibleTo"), "Tests erreichen interne Logik ohne öffentliche API."],
        ["Paketnamen", "Kern ohne Provider, Provider-Paket mit Suffix (" +
         c(".PostgreSQL") + ", " + c(".SqlServer") + ")."],
    ], [1.4, 2.6])
S.append(p(
    "Zur Dokumentation gehören neben der API-Doku vor allem die Dinge, die Nutzer nicht im "
    "Code sehen: welche Einzeldienste ersetzt werden (Kombinierbarkeit), welche Event-IDs "
    "es gibt, welche Einschränkungen bestehen (Pooling) und welche Sicherheitsannahmen "
    "gelten (Datenbankrollen)."))

# ------------------------------------------------------------ 26
S += h1("Die Checkliste von A bis Z")
S.append(p("Für jedes neue Plugin, in der Reihenfolge der Entwicklung:"))
check = [
    ("Architektur", [
        "Stufe festlegen: Hilfsmethoden, Modell-Helfer, echtes Plugin oder Provider?",
        "Kern- und Provider-Paket trennen; Typen für Entitäts-Assemblies in den Kern.",
        "Für jeden Erweiterungspunkt zuerst ein Plugin-Interface suchen, erst dann ersetzen.",
    ]),
    ("Options", [
        "Extension immutable mit Kopierkonstruktor und virtuellem Clone.",
        "Alle modellrelevanten Optionen im Hash, nichts, was pro Context variiert.",
        "Validate für Voraussetzungen (Provider, Kombinationen).",
        "LogFragment und PopulateDebugInfo pflegen.",
        "Use-Methode im Namespace Microsoft.EntityFrameworkCore, generisch und nicht-generisch.",
    ]),
    ("Dienste", [
        "Registrierung über den ServicesBuilder (Lebensdauern aus EFs Tabelle).",
        "Einzeldienste dekorieren, wo das öffentliche Interface genügt (Kapitel 5).",
        "Sonst Replace, und die Logik öffentlich und virtuell anbieten.",
        "ISingletonOptions für Singleton-Dienste, doppelt registriert.",
        "App-Abhängigkeiten über AddInterceptors, Pooling-Verhalten dokumentieren.",
    ]),
    ("Modell", [
        "Annotations mit Präfix, nur einfache Werte, keine Defaults speichern.",
        "Fluent API nicht-generisch implementieren, generisch delegieren.",
        "Convention-Builder-Überladungen für ConfigurationSource.",
        "Conventions bei ModelFinalizing, wenn sie vom fertigen Modell abhängen.",
        "Attribute im Kernpaket, Attribut-Conventions im Provider-Paket.",
        "Validierung als IModelFinalizedConvention: Fehler werfen, Fragwürdiges warnen.",
    ]),
    ("Type Mappings", [
        "Geschützter Parameter-Konstruktor und Clone-Überschreibung.",
        "Literal-Erzeugung für Modell- und Provider-Typ.",
        "Öffentliche statische Default-Property, gemeinsame Konverter-Instanz.",
        "JsonValueReaderWriter mit Instance und ConstructorExpression.",
    ]),
    ("Abfragen", [
        "EF.Functions-Methoden im Kern, Übersetzung im Provider-Paket.",
        "Translators: null für fremde Methoden, Nullability korrekt, Type Mappings setzen.",
        "IEvaluatableExpressionFilterPlugin für Methoden ohne Datenbezug.",
        "Keine Laufzeitwerte zur Übersetzungszeit einsetzen (Query-Cache, AOT).",
    ]),
    ("Laufzeit", [
        "Interceptors synchron und asynchron implementieren.",
        "Singleton-Interceptors zustandslos und thread-sicher.",
        "Eigene Event-IDs in eigenem Bereich, EventDefinitions gecacht.",
        "Sicherheitsrelevantes als Warnung, eskalierbar per ConfigureWarnings.",
        "Wo Hooks nicht laufen, eine explizite API anbieten (z. B. ApplyTenantSession).",
    ]),
    ("Migrations & Design-Time", [
        "Deklarativ über Annotation-Provider (nur Design-Time) und SQL-Generator.",
        "Bezeichner maskieren, Literale über Type Mappings, selbst erzeugtes SQL validieren.",
        "Eigene Operationen idempotent, Extensions im Migrations-Namespace.",
        "DesignTimeServicesReference mit forProvider; Code-Generator entfernt Annotations.",
        "Compiled Models prüfen: Default, Instance, Laufzeit-Annotations.",
    ]),
    ("Qualität", [
        "Modell-, SQL-, Snapshot-Roundtrip- und Integrationstests.",
        "Caching-Test mit verschiedenen Optionen; Test auf keine Phantom-Migration.",
        "Versionsbereich je EF-Major, CI gegen Previews, EF1001 bewusst.",
        "XML-Doku, SourceLink, Symbole, README, CHANGELOG, Einschränkungen dokumentiert.",
        "Serialisierte Annotation-Formate mit Golden-Value-Tests festschreiben.",
        "Zusammen mit verbreiteten Plugins testen (Naming Conventions, Decorators).",
    ]),
    ("Fortgeschritten (Teil VIII)", [
        "Wege dokumentieren, die Interceptors umgehen (ExecuteUpdate, rohes SQL).",
        "Value Generators mit HasValueGeneratorFactory(Type) konfigurieren (Compiled Models).",
        "Conventions für Complex Types und JSON-Spalten prüfen.",
        "Reihenfolge eigener Migrations-Operationen dokumentieren und testen.",
        "Idempotente Skripte und suppressTransaction berücksichtigen.",
        "Betriebsannahmen dokumentieren (Rollen, Pooling, Transaction-Pooling).",
        "Context-Lebensdauern dokumentieren: Factories, Hintergrundjobs, offene Verbindungen.",
        "Mandantenübergreifender Zugriff nur über eigene Rollen, nie über App-Schalter.",
        "Heiße Pfade (Interceptors) messen, nicht vermuten.",
        "CI mit Preview-Build und Public-API-Analyzer.",
    ]),
    ("Tiefe Erweiterungen (Teil IX)", [
        "Eigene SQL-Knoten: VisitChildren, Update, Quote, Print, Equals/GetHashCode.",
        "Eigene Knoten im SQL-Generator UND im Nullability-Prozessor behandeln.",
        "Aggregate: Prädikat, DISTINCT und leere Mengen bewusst behandeln.",
        "Laufzeit-Annotations bewusst wählen, Design-Time-Annotations filtern.",
        "Scaffolding: Schema-Objekte des Plugins aus der Datenbank lesen.",
        "Provider-neutrale Bausteine in den Kern, Unterschiede ins Provider-Paket.",
    ]),
]
for group, items in check:
    S += h2(group)
    for item in items:
        S.append(Paragraph("☐&nbsp;&nbsp;" + item, BODY_TIGHT))
    S.append(Spacer(1, 4))

