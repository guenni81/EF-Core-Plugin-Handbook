# ============================================================ TEIL VI
S += part(
    "Migrations und Design-Time",
    "Schema-Änderungen erzeugen, die Werkzeuge von dotnet ef erweitern und Compiled Models "
    "unterstützen.",
    ["Kapitel 17: Wie Migrations funktionieren",
     "Kapitel 18: Deklaratives SQL aus Annotations",
     "Kapitel 19: Eigene Migrations-Operationen",
     "Kapitel 20: Design-Time-Services",
     "Kapitel 21: Compiled Models, Trimming und NativeAOT",
     "Kapitel 22: Scaffolding (Reverse Engineering)"])

# ------------------------------------------------------------ 17
S += h1("Wie Migrations funktionieren")
S.append(p(
    "Migrations vergleichen zwei Modelle und erzeugen daraus Operationen und SQL. Ein "
    "Plugin kann sich an mehreren Stellen einklinken; um die richtige zu wählen, muss "
    "man den Ablauf kennen:"))
S += flow_diagram([
    ("Design-Time-Modell", "Modell inklusive aller Design-Time-Annotations"),
    ("IRelationalAnnotationProvider", "Überträgt Annotations auf Tabellen, Spalten, Indizes "
                                       "des relationalen Modells"),
    ("IMigrationsModelDiffer", "Vergleicht altes (Snapshot) und neues relationales Modell"),
    ("MigrationOperations", "CreateTable, AlterTable, ... mit .Annotation / .OldAnnotation"),
    ("ICSharpMigrationsGenerator", "Schreibt Migration, Designer-Datei und Snapshot (dotnet ef)"),
    ("IMigrationsSqlGenerator", "Erzeugt SQL beim Anwenden oder Skripten"),
])
S.append(p(
    "Für Plugins gibt es zwei grundsätzliche Strategien. <b>Deklarativ</b>: Das Plugin "
    "beschreibt den gewünschten Zustand als Annotations; EF erkennt Änderungen von selbst, "
    "und das Plugin muss nur SQL erzeugen. <b>Imperativ</b>: Das Plugin bietet eigene "
    "Operationen an, die Nutzer von Hand in Migrationen schreiben. Gute Plugins nutzen beide: "
    "deklarativ für alles, was zum Modell gehört, imperativ für alles andere."))
S += callout("hinweis", [
    c("Database.EnsureCreated()") + " nutzt dieselbe Pipeline: Es berechnet den Diff vom "
    "leeren Zustand zum Design-Time-Modell und führt das SQL des Migrations-Generators aus. "
    "Eigene Migrations-SQL eines Plugins wirkt also auch dort, was Integrationstests "
    "erheblich vereinfacht (Kapitel 23).",
])

# ------------------------------------------------------------ 18
S += h1("Deklaratives SQL aus Annotations")
S += h2("Der Annotation-Provider")
S.append(p(
    "Der Differ vergleicht das <i>relationale</i> Modell (Tabellen, Spalten), nicht die "
    "Entity-Typen. Welche Annotations eine Tabelle trägt, entscheidet der " +
    c("IRelationalAnnotationProvider") + ". Den gibt es pro Context einmal, und er gehört "
    "dem Provider; das Plugin erbt deshalb von der Npgsql-Implementierung:"))
S += code_block('''
public class TenancyAnnotationProvider : NpgsqlAnnotationProvider
{
    public TenancyAnnotationProvider(RelationalAnnotationProviderDependencies dependencies)
        : base(dependencies)
    {
    }

    public override IEnumerable<IAnnotation> For(ITable table, bool designTime)
    {
        foreach (var annotation in base.For(table, designTime))
            yield return annotation;

        if (!designTime)          // nur für Migrations: Laufzeitmodell bleibt schlank
            yield break;

        var entityType = (IEntityType)table.EntityTypeMappings.First().TypeBase;
        foreach (var annotation in entityType.GetAnnotations())
        {
            if (!annotation.Name.StartsWith(RlsAnnotationNames.Prefix,
                                            StringComparison.Ordinal))
                continue;
            // Opt-out-Marker nicht weitergeben, sonst entstehen leere Migrationen
            if (annotation.Name == RlsAnnotationNames.Enabled && annotation.Value is false)
                continue;
            yield return annotation;
        }
    }
}
''', label="Metadata/Internal/TenancyAnnotationProvider.cs")
S.append(p(
    "Ab hier übernimmt EF: Ändert sich eine Policy, erzeugt der Differ eine " +
    c("AlterTableOperation") + " mit neuen und alten Annotations. In der generierten "
    "Migration sieht das so aus, ganz ohne eigenen Code-Generator:"))
S += code_block('''
migrationBuilder.AlterTable(name: "Orders")
    .Annotation("Acme:Rls:Policy:tenant_isolation", "{...neu...}")
    .OldAnnotation("Acme:Rls:Policy:tenant_isolation", "{...alt...}");
''', label="Generierte Migration (Ausschnitt)")

S += h2("Der SQL-Generator")
S.append(p(
    "Auch den " + c("IMigrationsSqlGenerator") + " gibt es nur einmal. Das Plugin erbt vom "
    "Npgsql-Generator und ergänzt " + c("CreateTable") + " und " + c("AlterTable") + ":"))
S += code_block('''
protected override void Generate(
    CreateTableOperation operation, IModel? model,
    MigrationCommandListBuilder builder, bool terminate = true)
{
    // Unterminiertes SQL kann keine weiteren Statements aufnehmen (wie bei Npgsql)
    if (!terminate && operation.HasRlsConfiguration())
        throw new ArgumentException(
            "RLS kann nicht in unterminiertem CREATE TABLE entstehen.");

    base.Generate(operation, model, builder, terminate);
    if (terminate)
        GenerateRowLevelSecurity(operation.Name, operation.Schema, previous: null,
            operation, builder);
}

protected override void Generate(
    AlterTableOperation operation, IModel? model, MigrationCommandListBuilder builder)
{
    base.Generate(operation, model, builder);
    GenerateRowLevelSecurity(operation.Name, operation.Schema, operation.OldTable,
        operation, builder);
}
''', label="Migrations/Internal/TenancyMigrationsSqlGenerator.cs (Auszug)")
S += code_block('''
protected virtual void GenerateRowLevelSecurity(string tableName, string? schema,
    IReadOnlyAnnotatable? previous, IReadOnlyAnnotatable current,
    MigrationCommandListBuilder builder)
{
    var table = Delimit(tableName, schema);
    var oldPolicies = previous?.GetRlsPolicies() ?? new Dictionary<string, string>();
    var newPolicies = current.GetRlsPolicies();

    // 1. Entfernte oder geänderte Policies löschen
    foreach (var (name, json) in oldPolicies)
        if (!newPolicies.TryGetValue(name, out var n) || n != json)
            Statement(builder, $"DROP POLICY {Delimit(name)} ON {table}");

    // 2. RLS-Status (ENABLE/DISABLE, FORCE/NO FORCE)
    if ((previous?.IsRlsEnabled() ?? false) != current.IsRlsEnabled())
        Statement(builder, $"ALTER TABLE {table} "
            + (current.IsRlsEnabled() ? "ENABLE" : "DISABLE") + " ROW LEVEL SECURITY");
    // ... FORCE analog

    // 3. Neue oder geänderte Policies anlegen
    foreach (var (name, json) in newPolicies)
        if (!oldPolicies.TryGetValue(name, out var o) || o != json)
            Statement(builder, CreatePolicySql(table, RlsPolicy.Deserialize(json)));
}

protected virtual void Statement(MigrationCommandListBuilder builder, string sql)
{
    builder.Append(sql).AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
    EndStatement(builder);
}
''', label="Migrations/Internal/TenancyMigrationsSqlGenerator.cs (gekürzt)")
S += data_table(
    ["Detail", "Begründung"],
    [
        ["DROP + CREATE statt " + c("ALTER POLICY"), c("ALTER POLICY") + " kann weder " +
         c("FOR") + " noch PERMISSIVE/RESTRICTIVE ändern. DDL ist in PostgreSQL "
         "transaktional; es entsteht keine sichtbare Lücke."],
        [c("EndStatement(builder)"), "Beendet den Befehl wie die Basisklasse; berücksichtigt "
         "dabei Transaktionsunterdrückung."],
        ["Eigene " + c("Delimit") + "-Helfer", "Npgsqls " + c("DelimitIdentifier") +
         "-Helfer sind privat. Bezeichner immer über " + c("Dependencies.SqlGenerationHelper") +
         " maskieren, nie per String-Verkettung."],
        ["DropTable, RenameTable", "Nichts zu tun: PostgreSQL entfernt bzw. verschiebt "
         "Policies mit der Tabelle."],
    ], [1.3, 3])

# ------------------------------------------------------------ 19
S += h1("Eigene Migrations-Operationen")
S.append(p(
    "Manches gehört nicht ins Modell, aber in Migrationen: Rollen, Rechte, Extensions, "
    "Daten-Backfills. Dafür bietet das Plugin eigene Operationen an. Sie bestehen aus drei "
    "Teilen: der Operationsklasse, einer Extension-Method auf " + c("MigrationBuilder") +
    " und der SQL-Erzeugung im Generator."))
S += code_block('''
public class CreateApplicationRoleOperation : MigrationOperation
{
    public required string Name { get; init; }
    public bool CanLogin { get; init; }       // Passwörter gehören NICHT in Migrationen
}
''', label="Migrations/Operations/TenancyOperations.cs (Auszug)")
S += code_block('''
namespace Microsoft.EntityFrameworkCore.Migrations;      // in Migrationen immer importiert

public static class TenancyMigrationBuilderExtensions
{
    public static OperationBuilder<CreateApplicationRoleOperation> CreateApplicationRole(
        this MigrationBuilder migrationBuilder, string name, bool canLogin = false)
    {
        var operation = new CreateApplicationRoleOperation
        {
            Name = TenancyIdentifiers.ValidateIdentifier(name, "Rollenname"),
            CanLogin = canLogin,
        };
        migrationBuilder.Operations.Add(operation);
        return new OperationBuilder<CreateApplicationRoleOperation>(operation);
    }
}
''', label="Migrations/Operations/TenancyMigrationBuilderExtensions.cs (Auszug)")
S.append(p(
    "Der Generator verteilt Operationen über die zentrale " + c("Generate(MigrationOperation, ...)") +
    "-Methode. Unbekannte Typen gehen an die Basisklasse:"))
S += code_block('''
protected override void Generate(
    MigrationOperation operation, IModel? model, MigrationCommandListBuilder builder)
{
    switch (operation)
    {
        case CreateApplicationRoleOperation createRole: Generate(createRole, builder); break;
        case DropApplicationRoleOperation dropRole:     Generate(dropRole, builder); break;
        case GrantTablePrivilegesOperation grant:       Generate(grant, builder); break;
        default: base.Generate(operation, model, builder); break;
    }
}

protected virtual void Generate(CreateApplicationRoleOperation operation,
    MigrationCommandListBuilder builder)
{
    var literal = Literal(operation.Name);           // 'shop_app'  (für den Vergleich)
    var identifier = Delimit(operation.Name);    // shop_app (CREATE ROLE, ggf. in "...")
    var login = operation.CanLogin ? "LOGIN" : "NOLOGIN";

    builder
        .AppendLine("DO $$")
        .AppendLine("BEGIN")
        .AppendLine("    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles")
        .AppendLine($"                   WHERE rolname = {literal}) THEN")
        .AppendLine($"        CREATE ROLE {identifier} {login} NOSUPERUSER NOBYPASSRLS;")
        .AppendLine("    END IF;")
        .AppendLine("END")
        .Append("$$");
    builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
    EndStatement(builder);
}
''', label="Migrations/Internal/TenancyMigrationsSqlGenerator.cs (Auszug)")
S += callout("wichtig", [
    "Der Rollenname kommt zweimal vor, aber in zwei Formen: als <b>String-Literal</b> im "
    "Vergleich (" + c("'shop_app'") + ", erzeugt über das String-Type-Mapping) und als "
    "<b>Bezeichner</b> in " + c("CREATE ROLE") + " (" + c("shop_app") + ", bei Bedarf in doppelten Anführungszeichen). Wer beides "
    "verwechselt oder per String-Verkettung baut, erzeugt ungültiges SQL oder eine "
    "Injection-Lücke. Zusätzlich validiert die Extension-Method den Namen.",
], title="Literal oder Bezeichner")
S += callout("tipp", [
    "Operationen idempotent formulieren (" + c("IF NOT EXISTS") + ", " + c("DROP ... IF EXISTS") +
    "). Rollen existieren clusterweit und überleben Datenbanken; eine Migration, die beim "
    "zweiten Ausführen in einer neuen Datenbank scheitert, ist ein häufiger Ärger.",
])
S.append(p(
    "In einer Migration nutzt man die Operationen wie EFs eigene:"))
S += code_block('''
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.CreateApplicationRole("shop_support");  // VOR den Tabellen (Kap. 32)
    // ... von EF generierte Operationen (Tabellen, RLS über Annotations) ...
    migrationBuilder.CreateApplicationRole("shop_app", canLogin: true);
    migrationBuilder.GrantTablePrivileges("shop_app");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropApplicationRole("shop_app");
    // ...
}
''', label="Migrations/20261021_Initial.cs (handergänzt)")
S += callout("hinweis", [
    "Sollen eigene Operationen vom <i>Differ</i> automatisch erzeugt werden, braucht es "
    "einen abgeleiteten " + c("IMigrationsModelDiffer") + " und einen " +
    c("ICSharpMigrationOperationGenerator") + ", der die Operation als C# schreibt. Beides "
    "sind Einzeldienste mit viel interner API. Fast immer ist der deklarative Weg über "
    "Annotations (Kapitel 18) die bessere Wahl.",
], title="Wenn der Differ eigene Operationen erzeugen soll")

# ------------------------------------------------------------ 20
S += h1("Design-Time-Services")
S.append(p(
    "Die Werkzeuge von " + c("dotnet ef") + " (Migrationen anlegen, Snapshots schreiben, "
    "Scaffolding, Compiled Models) verwenden einen eigenen Service Provider mit "
    "Design-Time-Diensten. Ein Plugin erweitert ihn über " + c("IDesignTimeServices") + "."))
S += h2("Automatisch geladen")
S.append(p(
    "Damit Nutzer nichts konfigurieren müssen, verweist ein Assembly-Attribut auf die "
    "Klasse. Das Tooling findet es in jeder Assembly, die das Startprojekt referenziert. "
    "Das zweite Argument beschränkt das Laden auf Contexts mit dem passenden Provider:"))
S += code_block('''
[assembly: DesignTimeServicesReference(
    "Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Design.TenancyDesignTimeServices, "
        + "Acme.EntityFrameworkCore.Tenancy.PostgreSQL",
    "Npgsql.EntityFrameworkCore.PostgreSQL")]
''', label="Properties/AssemblyInfo.cs")
S += code_block('''
public class TenancyDesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection services)
    {
        // Alle Registrierungen entfernen (EF-Default, Npgsql), dann unsere setzen:
        // unabhängig von der Reihenfolge, in der das Tooling Services lädt
        services.RemoveAll<IAnnotationCodeGenerator>();
        services.AddSingleton<IAnnotationCodeGenerator, TenancyAnnotationCodeGenerator>();
    }
}
''', label="Design/TenancyDesignTimeServices.cs")

S += h2("Lesbare Snapshots: der Annotation-Code-Generator")
S.append(p(
    "Ohne Zutun schreibt EF unbekannte Annotations als " + c("HasAnnotation(...)") + " in "
    "den Snapshot, bei Policies mit JSON-Werten. Das funktioniert, ist aber kaum lesbar. "
    "Ein eigener " + c("IAnnotationCodeGenerator") + " übersetzt sie in die Fluent API:"))
S += code_block('''
// Vorher
b.HasAnnotation("Acme:Rls:Enabled", true);
b.HasAnnotation("Acme:Rls:Policy:tenant_isolation", "{\\"Name\\":\\"tenant_isolation\\",...}");

// Nachher
RlsEntityTypeBuilderExtensions.HasRowLevelSecurity(b, true);
RlsEntityTypeBuilderExtensions.HasPolicy(b, "tenant_isolation", p => p
    .Using("\\"TenantId\\" = nullif(current_setting('app.tenant_id', true), '')::uuid")
    .WithCheck("\\"TenantId\\" = nullif(current_setting('app.tenant_id', true), '')::uuid"));
''', label="ShopContextModelSnapshot.cs")
S.append(p(
    "EF Core 10 schreibt Erweiterungsmethoden im Snapshot als statische Aufrufe (" +
    c("Extensions.Methode(b, ...)") + ") statt als " + c("b.Methode(...)") + ". Das vermeidet "
    "Mehrdeutigkeiten, wenn mehrere Provider oder Plugins gleichnamige Methoden definieren. "
    "Tests, die den erzeugten Code prüfen, sollten diese Form erwarten."))
S += code_block('''
public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
    IEntityType entityType, IDictionary<string, IAnnotation> annotations)
{
    var calls = new List<MethodCallCodeFragment>();

    // VERTRAG: Übersetzte Annotations aus dem Dictionary ENTFERNEN
    if (annotations.TryGetValue(RlsAnnotationNames.Enabled, out var enabled)
        && enabled.Value is true)
    {
        annotations.Remove(RlsAnnotationNames.Enabled);
        var forced = annotations.Remove(RlsAnnotationNames.Forced, out var f)
                     && f.Value is true;
        calls.Add(new MethodCallCodeFragment(HasRowLevelSecurityMethod, forced));
    }

    foreach (var key in annotations.Keys
                 .Where(k => k.StartsWith(RlsAnnotationNames.PolicyPrefix,
                                          StringComparison.Ordinal))
                 .ToList())                         // Kopie: Dictionary ändert sich
    {
        var policy = RlsPolicy.Deserialize((string)annotations[key].Value!);
        annotations.Remove(key);
        calls.Add(new MethodCallCodeFragment(HasPolicyMethod, policy.Name,
            new NestedClosureCodeFragment("p", BuildPolicyChain(policy))));
    }

    calls.AddRange(base.GenerateFluentApiCalls(entityType, annotations));
    return calls;
}
''', label="Design/Internal/TenancyAnnotationCodeGenerator.cs (Auszug)")
S += data_table(
    ["Stolperstelle", "Lösung"],
    [
        ["Übersetzte Annotation bleibt im Dictionary", "Erscheint doppelt im Snapshot. Immer "
         "entfernen; was übrig bleibt, schreibt EF als " + c("HasAnnotation") + "."],
        ["Generische Methode im " + c("MethodInfo"), "Der Snapshot nutzt den nicht-generischen " +
         c("EntityTypeBuilder") + ". Nicht-generische Überladung per " +
         c("GetRuntimeMethod") + " mit exakten Parametertypen wählen."],
        [c("params")+"-Argument als Array", c("MethodCallCodeFragment") + " nimmt " +
         c("params object?[]") + ". Ein " + c("string[]") + " würde wegen Array-Kovarianz zur "
         "Argumentliste: in " + c("new object?[] { roles }") + " verpacken."],
        ["Enums im Snapshot", "Werden als " + c("PolicyCommand.Select") + " gerendert. Das "
         "Enum liegt deshalb im immer importierten Namespace " + c("Microsoft.EntityFrameworkCore") + "."],
        ["Lambda-Argumente", c("NestedClosureCodeFragment(\"p\", kette)") + " erzeugt " +
         c("p => p.Using(...)") + "; Ketten entstehen mit " + c("fragment.Chain(...)") + "."],
    ], [1.4, 2.6])
S.append(p(
    "Der Generator betrifft nur Snapshot und Designer-Dateien. Die eigentlichen Migrationen "
    "behalten " + c(".Annotation(...)") + " an " + c("AlterTable") + "; das ist der "
    "vorgesehene Mechanismus für Änderungen."))

# ------------------------------------------------------------ 21
S += h1("Compiled Models, Trimming und NativeAOT")
S.append(p(
    c("dotnet ef dbcontext optimize") + " erzeugt ein <b>Compiled Model</b>: C#-Code, der "
    "das Laufzeitmodell direkt aufbaut, statt es beim Start über Conventions zu berechnen. "
    "Das verkürzt die Startzeit großer Modelle deutlich und ist Voraussetzung für NativeAOT. "
    "Ein Plugin muss dafür an vier Stellen vorbereitet sein:"))
S += data_table(
    ["Baustein", "Anforderung", "Im Beispiel"],
    [
        ["Type Mappings", "Öffentlicher Typ mit öffentlicher statischer Property " +
         c("Default") + " (für NativeAOT genutzt); Konverter/Comparer als gemeinsame Instanzen",
         c("TenantIdTypeMapping.Default") + ", " + c("TenantIdConverter.Instance")],
        ["JSON-Reader/Writer", "Statische " + c("Instance") + " und " +
         c("ConstructorExpression"), c("TenantIdJsonReaderWriter")],
        ["Value Generators", "Konfiguriert mit " + c("HasValueGeneratorFactory(Type)") +
         "; Factory mit öffentlichem parameterlosem Konstruktor",
         c("TenantIdValueGeneratorFactory") + " (Kapitel 28)"],
        ["Annotations", "Einfache Werte übernimmt EF automatisch; ein " +
         c("ICSharpRuntimeAnnotationCodeGenerator") + " filtert Design-Time-Annotations und "
         "übersetzt komplexe Werte", "Filtert RLS-Policies, behält die Laufzeit-Annotation "
         "(Kapitel 39)"],
    ], [1.1, 2.2, 1.7])
S.append(p(
    "Der dritte Punkt wird oft missverstanden. EF übernimmt Annotations mit einfachen "
    "Werten (Strings, Zahlen, bool, Enums) <b>automatisch</b> ins Compiled Model; das zeigt "
    "der Quellcode von " + c("CSharpRuntimeAnnotationCodeGenerator") + ", der sie mit " +
    c("GenerateSimpleAnnotations") + " schreibt. Ein eigener Generator ist deshalb nicht "
    "nötig, damit Annotations <i>erhalten</i> bleiben, sondern um reine Design-Time-"
    "Annotations <i>herauszufiltern</i>, die zur Laufzeit niemand liest, und um Annotations "
    "mit komplexen Werten in Code zu übersetzen. Kapitel 39 zeigt beides im Beispiel."))

S += h2("Trimming und AOT")
S.append(p(
    "Beide Pakete setzen " + c("<IsAotCompatible>true</IsAotCompatible>") + ". Der Compiler "
    "meldet dann Code, der Reflection ohne Annotationen nutzt. Die JSON-Serialisierung der "
    "Policies verwendet einen Source-Generator statt Reflection:"))
S += code_block('''
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(RlsPolicy))]
internal partial class RlsJsonContext : JsonSerializerContext;
''', label="Metadata/RlsPolicy.cs (Auszug)")
S += callout("hinweis", [
    "Conventions, Snapshots und Migrations laufen nur zur Design-Zeit oder beim "
    "Modellbau; mit Compiled Model und vorkompilierten Abfragen fallen sie zur Laufzeit "
    "weg. Für AOT kritisch sind deshalb vor allem Type Mappings, Konverter, Translators "
    "(laufen beim Build) und Interceptors.",
])

# ------------------------------------------------------------ 22
S += h1("Scaffolding (Reverse Engineering)")
S.append(p(
    c("dotnet ef dbcontext scaffold") + " liest ein bestehendes Datenbankschema und erzeugt "
    "Entitäten und einen Context. Ein Plugin, das eigene Schema-Elemente verwaltet, kann "
    "sie dabei ebenfalls erkennen. Der Weg führt über zwei Design-Time-Dienste:"))
S += data_table(
    ["Dienst", "Aufgabe"],
    [
        [c("IDatabaseModelFactory"), "Liest das Schema in ein " + c("DatabaseModel") +
         ". Ein abgeleiteter Npgsql-Factory könnte " + c("pg_policies") + " und " +
         c("pg_class.relrowsecurity") + " abfragen und als Annotations an " +
         c("DatabaseTable") + " hängen."],
        [c("IAnnotationCodeGenerator"), "Schreibt diese Annotations als Fluent API in den "
         "generierten " + c("OnModelCreating") + ", mit derselben Logik wie beim Snapshot."],
    ], [1.3, 3])
S.append(p(
    "Das Beispiel implementiert beides: Eine eigene Database-Model-Factory liest RLS-Status "
    "und Policies aus der Datenbank, und der Annotation-Code-Generator aus Kapitel 20 "
    "schreibt sie in den gescaffoldeten Context. Kapitel 40 zeigt die Umsetzung im Detail."))
