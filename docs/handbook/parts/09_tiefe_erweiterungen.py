# ============================================================ TEIL IX
S += part(
    "Tiefe Erweiterungen",
    "Eigene SQL-Ausdrücke, Aggregate, Laufzeit-Metadaten, Scaffolding und ein zweiter "
    "Provider: die Werkzeuge für sehr komplexe Plugins.",
    ["Kapitel 37: Eigene SQL-Ausdrucksknoten",
     "Kapitel 38: Aggregat-Translators",
     "Kapitel 39: Fluent API auf Property-Ebene und Laufzeit-Annotations",
     "Kapitel 40: Scaffolding in der Praxis",
     "Kapitel 41: Was in den Kern gehört",
     "Kapitel 42: Ein zweiter Provider: SQL Server"])

# ------------------------------------------------------------ 37
S += h1("Eigene SQL-Ausdrucksknoten")
S.append(p(
    "Kapitel 12 baut SQL aus vorhandenen Bausteinen: Funktionen, Konstanten, Casts. Das "
    "reicht für die meisten Plugins. Ein <b>eigener Ausdrucksknoten</b> (eine Ableitung von " +
    c("SqlExpression") + ") wird nötig, wenn mindestens eins davon zutrifft:"))
S += data_table(
    ["Grund", "Beispiel"],
    [
        ["Die Syntax ist kein Funktionsaufruf", "Operatoren wie " + c("@@") + ", " + c("<->") +
         ", " + c("IS DISTINCT FROM") + ", Schlüsselwörter wie " + c("AT TIME ZONE")],
        ["Die Nullability-Semantik weicht ab", c("IS DISTINCT FROM") + " ist nie NULL, auch "
         "wenn ein Operand NULL ist"],
        ["Andere Teile sollen den Knoten wiedererkennen", "Optimierungen, die nur für diesen "
         "Ausdruck gelten"],
    ], [1.6, 2.4])
S.append(p(
    "Das Beispiel implementiert den null-sicheren Vergleich " +
    c("EF.Functions.IsNotDistinctFrom(a, b)") + " und " + c("IsDistinctFrom(a, b)") +
    ". Npgsql bietet ihn nicht als Funktion an; er erfüllt alle drei Gründe."))

S += h2("Anatomie eines Knotens")
S += data_table(
    ["Mitglied", "Zweck", "Folge, wenn es fehlt"],
    [
        [c("VisitChildren") + " / " + c("Update"), "Visitors können Kinder ersetzen; "
         "unveränderte Kinder ergeben dieselbe Instanz", "EF wirft beim ersten Visitor"],
        [c("Equals") + " / " + c("GetHashCode"), "Strukturvergleich für den Query-Cache und "
         "Optimierungen", "Cache-Fehltreffer, falsche Deduplizierung"],
        [c("Print"), "Lesbare Darstellung in DebugView und Fehlermeldungen", "Unklare Fehler"],
        [c("Quote"), "C#-Code für vorkompilierte Abfragen und NativeAOT", "Kein AOT-Support"],
        ["SQL-Generator", "Schreibt den Knoten als SQL", "<b>Stillschweigend</b> fehlendes SQL"],
        ["Nullability-Prozessor", "Kennt die NULL-Semantik", "Exception beim Übersetzen"],
    ], [1.3, 2.1, 1.6])
S += code_block('''
public class DistinctFromExpression : SqlExpression
{
    private static ConstructorInfo? _quotingConstructor;

    public DistinctFromExpression(SqlExpression left, SqlExpression right, bool negated,
        RelationalTypeMapping? typeMapping)
        : base(typeof(bool), typeMapping)
    {
        // ... ThrowIfNull ...
        Left = left;
        Right = right;
        IsNegated = negated;
    }

    public virtual SqlExpression Left { get; }
    public virtual SqlExpression Right { get; }
    public virtual bool IsNegated { get; }

    protected override Expression VisitChildren(ExpressionVisitor visitor)
        => Update((SqlExpression)visitor.Visit(Left), (SqlExpression)visitor.Visit(Right));

    public virtual DistinctFromExpression Update(SqlExpression left, SqlExpression right)
        => left == Left && right == Right
            ? this
            : new DistinctFromExpression(left, right, IsNegated, TypeMapping);

    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DistinctFromExpression).GetConstructor(
                [typeof(SqlExpression), typeof(SqlExpression), typeof(bool),
                 typeof(RelationalTypeMapping)])!,
            Left.Quote(), Right.Quote(), Constant(IsNegated),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));

    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Visit(Left);
        expressionPrinter.Append(IsNegated ? " IS DISTINCT FROM " : " IS NOT DISTINCT FROM ");
        expressionPrinter.Visit(Right);
    }

    public override bool Equals(object? obj)
        => obj is DistinctFromExpression other
           && (ReferenceEquals(this, other)
               || base.Equals(other) && Left.Equals(other.Left)
                  && Right.Equals(other.Right) && IsNegated == other.IsNegated);

    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), Left, Right, IsNegated);
}
''', label="Query/Expressions/DistinctFromExpression.cs (gekürzt)")
S.append(p(
    "Der Translator erzeugt den Knoten und gleicht dabei die Type Mappings beider Seiten "
    "an: Hat eine Seite ein Mapping (etwa eine Spalte), bekommt die andere dasselbe. So "
    "wird aus einer Konstante das passende Literal:"))
S += code_block('''
var definition = method.GetGenericMethodDefinition();
if (definition != IsNotDistinctFromMethod && definition != IsDistinctFromMethod)
    return null;

var left = arguments[1];                        // arguments[0] ist DbFunctions
var right = arguments[2];
var typeMapping = ExpressionExtensions.InferTypeMapping(left, right)
                  ?? _typeMappingSource.FindMapping(left.Type);

return new DistinctFromExpression(
    _sql.ApplyTypeMapping(left, typeMapping)!,
    _sql.ApplyTypeMapping(right, typeMapping)!,
    negated: definition == IsDistinctFromMethod,
    _typeMappingSource.FindMapping(typeof(bool)));
''', label="Query/DistinctFromTranslator.cs (Auszug)")

S += h2("Den SQL-Generator erweitern")
S.append(p(
    "Der " + c("QuerySqlGenerator") + " schreibt SQL per Visitor. Unbekannte Knoten gibt er "
    "an " + c("ExpressionVisitor.VisitExtension") + " weiter, das nur die Kinder besucht. "
    "Ohne eigenen Override entsteht also kein Fehler, sondern <b>SQL ohne den Operator</b>. "
    "Das Plugin erbt vom Npgsql-Generator:"))
S += code_block('''
public class TenancyNpgsqlQuerySqlGenerator : NpgsqlQuerySqlGenerator
{
    public TenancyNpgsqlQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource,
        bool reverseNullOrderingEnabled, Version postgresVersion)
        : base(dependencies, typeMappingSource, reverseNullOrderingEnabled, postgresVersion)
    {
    }

    protected override Expression VisitExtension(Expression extensionExpression)
        => extensionExpression switch
        {
            DistinctFromExpression e => VisitDistinctFrom(e),
            _ => base.VisitExtension(extensionExpression),
        };

    protected virtual Expression VisitDistinctFrom(DistinctFromExpression expression)
    {
        VisitOperand(expression.Left);
        Sql.Append(expression.IsNegated ? " IS DISTINCT FROM " : " IS NOT DISTINCT FROM ");
        VisitOperand(expression.Right);
        return expression;
    }
}
''', label="Query/Internal/TenancyNpgsqlQuerySqlGenerator.cs (gekürzt)")
S.append(p(
    "Der Generator wird über " + c("IQuerySqlGeneratorFactory") + " erzeugt, einen "
    "Singleton-Einzeldienst. Das Plugin ersetzt ihn durch eine eigene Factory mit denselben "
    "Abhängigkeiten wie die von Npgsql (" + c("QuerySqlGeneratorDependencies") + ", Type-"
    "Mapping-Source, " + c("INpgsqlSingletonOptions") + ")."))

S += h2("Den Nullability-Prozessor erweitern")
S.append(p(
    "Nach der Übersetzung optimiert EF NULL-Vergleiche. Dafür muss es von jedem Knoten "
    "wissen, ob er NULL werden kann. Unbekannte Knoten landen in " +
    c("VisitCustomSqlExpression") + ", dessen Basisimplementierung eine Exception wirft. Der "
    "Prozessor hängt an einer Kette aus drei Klassen, die alle ersetzt werden müssen:"))
S += code_block('''
public class TenancyNpgsqlSqlNullabilityProcessor : NpgsqlSqlNullabilityProcessor
{
    // Konstruktor: (…ProcessorDependencies, …ProcessorParameters) : base(...)

    protected override SqlExpression VisitCustomSqlExpression(
        SqlExpression sqlExpression, bool allowOptimizedExpansion, out bool nullable)
    {
        if (sqlExpression is DistinctFromExpression distinctFrom)
        {
            // Kinder besuchen (sie können optimiert werden), ihre Nullability ist egal
            var left = Visit(distinctFrom.Left, out _)!;
            var right = Visit(distinctFrom.Right, out _)!;

            nullable = false;                   // IS [NOT] DISTINCT FROM ist nie NULL
            return distinctFrom.Update(left, right);
        }

        return base.VisitCustomSqlExpression(
            sqlExpression, allowOptimizedExpansion, out nullable);
    }
}

public class TenancyNpgsqlParameterBasedSqlProcessor : NpgsqlParameterBasedSqlProcessor
{
    protected override Expression ProcessSqlNullability(
        Expression selectExpression, ParametersCacheDecorator parametersDecorator)
        => new TenancyNpgsqlSqlNullabilityProcessor(Dependencies, Parameters)
            .Process(selectExpression, parametersDecorator);
}

// + TenancyNpgsqlParameterBasedSqlProcessorFactory
//   : IRelationalParameterBasedSqlProcessorFactory
''', label="Query/Internal/TenancyNpgsqlSqlNullabilityProcessor.cs (gekürzt)")
S += code_block('''
// 4) Tiefe Erweiterung: eigener SQL-Ausdrucksknoten
services.Replace(ServiceDescriptor
    .Singleton<IQuerySqlGeneratorFactory, TenancyNpgsqlQuerySqlGeneratorFactory>());
services.Replace(ServiceDescriptor
    .Scoped<IRelationalParameterBasedSqlProcessorFactory,
        TenancyNpgsqlParameterBasedSqlProcessorFactory>());
''', label="Infrastructure/TenancyOptionsExtension.cs › ApplyServices (Ergänzung)")
S += callout("wichtig", [
    "Eigene Knoten verlangen das Ersetzen zweier Einzeldienste und das Erben von internen "
    "Npgsql-Klassen. Zwei Plugins, die beide eigene Knoten einführen, lassen sich nur "
    "kombinieren, wenn eines vom anderen erbt. Deshalb gilt: Funktionskomposition zuerst; "
    "eigene Knoten nur, wenn einer der drei Gründe vorliegt. Und die Ersetzung gehört an "
    "prominente Stelle in die Dokumentation.",
], title="Der Preis eigener Knoten")
S += callout("hinweis", [
    c("Quote") + " und " + c("RelationalExpressionQuotingUtilities") + " gehören zur "
    "experimentellen API für vorkompilierte Abfragen. Ihre Verwendung meldet der Compiler "
    "als EF9100; das Beispiel unterdrückt die Meldung projektweit und dokumentiert das in " +
    c("Directory.Build.props") + ".",
], title="Experimentelle API")
S += code_block('''
[Fact]
public void IsDistinctFrom_in_projection_passes_nullability_processor()
{
    // Ohne eigenen Nullability-Prozessor würde EF hier eine Exception werfen
    using var ctx = Contexts.Create();

    var sql = ctx.Customers
        .Select(c => EF.Functions.IsDistinctFrom(c.Name, (string?)null))
        .ToQueryString();

    Assert.Contains("IS DISTINCT FROM", sql);
}
''', label="tests/.../DeepQueryTests.cs (Auszug)")

# ------------------------------------------------------------ 38
S += h1("Aggregat-Translators")
S.append(p(
    "Aggregate wie " + c("Sum") + " oder " + c("string_agg") + " fassen mehrere Zeilen "
    "zusammen, typischerweise nach " + c("GroupBy") + ". Ein " +
    c("IAggregateMethodCallTranslator") + " bekommt statt eines einzelnen Arguments eine " +
    c("EnumerableExpression") + ", die beschreibt, <i>was</i> aggregiert wird:"))
S += data_table(
    ["Eigenschaft", "Bedeutung", "Herkunft in LINQ"],
    [
        [c("Selector"), "Der aggregierte Ausdruck", c("g.Select(o => o.Total > 0)")],
        [c("IsDistinct"), "Nur verschiedene Werte", c("g.Select(...).Distinct()")],
        [c("Predicate"), "Filter vor dem Aggregat", c("g.Where(o => o.Id > 10)")],
        [c("Orderings"), "Reihenfolge (für reihenfolgeabhängige Aggregate)",
         c("g.OrderBy(...)")],
    ], [1.1, 1.7, 1.9])
S.append(p(
    "Das Beispiel ergänzt " + c("EF.Functions.Every(values)") + " für PostgreSQLs " +
    c("bool_and") + ":"))
S += code_block('''
public virtual SqlExpression? Translate(MethodInfo method, EnumerableExpression source,
    IReadOnlyList<SqlExpression> arguments,
    IDiagnosticsLogger<DbLoggerCategory.Query> logger)
{
    if (method != EveryMethod || source.Selector is not SqlExpression selector)
        return null;

    // Npgsqls Factory setzt DISTINCT, FILTER (WHERE ...) und ORDER BY im Aggregat um
    if (_sql is not NpgsqlSqlExpressionFactory npgsql)
        return null;

    var boolean = _typeMappingSource.FindMapping(typeof(bool))!;

    // bool_and liefert für eine leere Menge NULL, Enumerable.All liefert true
    return _sql.Coalesce(
        npgsql.AggregateFunction("bool_and", [selector], source,
            nullable: true, argumentsPropagateNullability: [false],
            typeof(bool), boolean),
        _sql.Constant(true, boolean),
        boolean);
}
''', label="Query/EveryAggregateTranslator.cs (Auszug)")
S += code_block('''
SELECT COALESCE(bool_and(o."Total" > 0.0) FILTER (WHERE o."Id" > 10), TRUE)
FROM "Orders" AS o
GROUP BY o."TenantId"
''', lang="sql", label="Erzeugtes SQL für g.Where(o => o.Id > 10).Select(o => o.Total > 0)")
S += callout("wichtig", [
    "Ein Aggregat-Translator muss die Semantik der .NET-Seite treffen, nicht nur die Syntax "
    "der Datenbank. Typische Fallen: leere Mengen (NULL statt 0, true oder Leerstring), "
    "NULL-Werte in der Eingabe und Prädikate. Npgsql selbst gleicht etwa " +
    c("string.Join") + " mit " + c("COALESCE") + " an " + c("string_agg") + " an; das "
    "Beispiel folgt diesem Muster.",
], title="Semantik vor Syntax")
S.append(p(
    "Datenbanken ohne " + c("FILTER") + "-Klausel (etwa SQL Server) emulieren Prädikate mit " +
    c("CASE WHEN prädikat THEN wert END") + ". Ein provider-neutraler Aggregat-Translator "
    "muss beides kennen; deshalb liegt dieser im PostgreSQL-Paket."))

# ------------------------------------------------------------ 39
S += h1("Fluent API auf Property-Ebene und Laufzeit-Annotations")
S += h2("Property-Builder")
S.append(p(
    "Bisher setzt das Plugin voraus, dass die Mandanten-Property " + c("TenantId") + " heißt "
    "und " + c("ITenantOwned") + " implementiert ist. Mit einer Fluent-API-Methode auf " +
    c("PropertyBuilder") + " geht es ohne beides, etwa bei bestehenden Modellen mit einer "
    "Property " + c("MandantId") + " oder einer expliziten Interface-Implementierung:"))
S += code_block('''
modelBuilder.Entity<Contract>().Property(c => c.Mandant).IsTenantProperty();
''', label="Anwendung")
S += code_block('''
public static PropertyBuilder IsTenantProperty(
    this PropertyBuilder builder, bool isTenant = true)
{
    ArgumentNullException.ThrowIfNull(builder);

    if (isTenant)
        builder.Metadata.SetAnnotation(TenancyAnnotationNames.IsTenantProperty, true);
    else
        builder.Metadata.RemoveAnnotation(TenancyAnnotationNames.IsTenantProperty);

    return builder;
}

public static PropertyBuilder<TProperty> IsTenantProperty<TProperty>(
    this PropertyBuilder<TProperty> builder, bool isTenant = true)
    => (PropertyBuilder<TProperty>)IsTenantProperty((PropertyBuilder)builder, isTenant);

public static IConventionPropertyBuilder? IsTenantProperty(
    this IConventionPropertyBuilder builder,
    bool isTenant = true,
    bool fromDataAnnotation = false)
    => builder.HasAnnotation(TenancyAnnotationNames.IsTenantProperty,
           isTenant ? true : null, fromDataAnnotation) is null
        ? null
        : builder;
''', label="Metadata/TenancyPropertyBuilderExtensions.cs (Kernpaket)")
S.append(p(
    "Die Methode liegt im Kernpaket, weil sie nur Metadaten setzt. Die Conventions beider "
    "Provider-Pakete suchen die Mandanten-Property über dieselbe Kernfunktion:"))
S += code_block('''
public static IReadOnlyProperty? FindTenantPropertyCandidate(
    this IReadOnlyEntityType entityType)
{
    var marked = entityType.GetProperties()
        .Where(p => p[TenancyAnnotationNames.IsTenantProperty] is true)
        .ToList();

    if (marked.Count > 1)
        throw new InvalidOperationException("... mehrere Mandanten-Properties ...");
    if (marked.Count == 1)
        return marked[0];

    return typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType)
        ? entityType.FindProperty(nameof(ITenantOwned.TenantId))
        : null;
}
''', label="Metadata/TenancyEntityTypeExtensions.cs (Kernpaket, gekürzt)")
S += data_table(
    ["Builder", "Typische Plugin-Methoden", "Hinweis"],
    [
        [c("PropertyBuilder") + " / " + c("<T>"), "Spaltenoptionen, Rollen von Properties",
         "Hier: " + c("IsTenantProperty")],
        [c("IndexBuilder") + " / " + c("<T>"), "Index-Methoden, Operator-Klassen, Filter",
         "Annotation an " + c("Metadata") + " (IMutableIndex)"],
        [c("KeyBuilder"), "Schlüsseloptionen (Clustering)", "Wie oben"],
        [c("ComplexTypePropertyBuilder"), "Properties in Complex Types", "Nur anbieten, wenn "
         "die Option dort Sinn ergibt (Kapitel 30)"],
        [c("ModelBuilder"), "Modellweite Optionen", "Annotation am Modell"],
    ], [1.5, 1.8, 1.6])

S += h2("Laufzeit-Annotations")
S.append(p(
    "Der SaveChanges-Interceptor muss zur Laufzeit wissen, welche Entitäten isoliert sind "
    "und wie ihre Mandanten-Property heißt. Die Convention schreibt das in eine Annotation "
    "am Entity-Typ; der Interceptor liest sie aus dem <i>Laufzeitmodell</i>:"))
S += code_block('''
// Convention (Design-Time-Modell)
entityType.Builder.HasAnnotation(TenancyAnnotationNames.TenantProperty, property.Name);

// Interceptor (Laufzeitmodell)
public static string? GetTenantPropertyName(this IReadOnlyEntityType entityType)
    => entityType.GetRootType()[TenancyAnnotationNames.TenantProperty] as string;
''', label="Convention und Kernpaket (Auszug)")
S.append(p(
    "Zwei Fragen entscheiden, ob das funktioniert. Landet die Annotation im Laufzeitmodell? "
    "Ja: " + c("RuntimeModelConvention") + " entfernt nur EFs eigene Design-Time-"
    "Annotations und übernimmt alle anderen. Landet sie im Compiled Model? Ja: Einfache "
    "Werte schreibt EF automatisch (Kapitel 21). Der eigene "
    + c("ICSharpRuntimeAnnotationCodeGenerator") + " hat deshalb die umgekehrte Aufgabe: Er "
    "entfernt die RLS-Annotations, die nur Migrations brauchen:"))
S += code_block('''
public class TenancyCSharpRuntimeAnnotationCodeGenerator
    : NpgsqlCSharpRuntimeAnnotationCodeGenerator
{
    // Konstruktor: (CSharpRuntimeAnnotationCodeGeneratorDependencies,
    //               RelationalCSharpRuntimeAnnotationCodeGeneratorDependencies) : base(...)

    public override void Generate(IEntityType entityType,
        CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        if (!parameters.IsRuntime)
        {
            foreach (var key in parameters.Annotations.Keys
                         .Where(k => k.StartsWith(RlsAnnotationNames.Prefix,
                                                  StringComparison.Ordinal))
                         .ToList())
            {
                parameters.Annotations.Remove(key);
            }
        }

        base.Generate(entityType, parameters);
    }
}
''', label="Design/Internal/TenancyCSharpRuntimeAnnotationCodeGenerator.cs (gekürzt)")
S.append(p(
    "Wird das Compiled Model für NativeAOT erzeugt (" + c("ForNativeAot") + "), schreibt EF "
    "zusätzlich das relationale Modell. Der Annotation-Provider hat die RLS-Annotations auf "
    "die Tabellen kopiert, sie tauchen dort also ein zweites Mal auf. Der Generator wendet "
    "deshalb denselben Filter in einer Überschreibung von " + c("Generate(ITable, ...)") +
    " an."))
S += data_table(
    ["Annotation", "Design-Time", "Laufzeit", "Compiled Model"],
    [
        [c("Acme:Rls:*") + " (Policies)", "ja (Migrations)", "ja, ungenutzt", "gefiltert"],
        [c("Acme:Tenancy:TenantProperty"), "ja", "ja (Interceptor)", "ja"],
        [c("Acme:Tenancy:IsTenantProperty"), "ja (Convention)", "ja, ungenutzt", "ja (klein)"],
    ], [1.7, 1.1, 1.1, 1.1])
S += callout("tipp", [
    "Für jede Annotation sollte klar sein, wer sie wann liest. Nur zur Design-Zeit gelesene "
    "Annotations gehören nicht ins Compiled Model; zur Laufzeit gelesene müssen hinein und "
    "brauchen einen Test gegen das generierte Modell. Der Compiled-Model-Test des Beispiels "
    "prüft beides.",
])

# ------------------------------------------------------------ 40
S += h1("Scaffolding in der Praxis")
S.append(p(
    "Beim Scaffolding (" + c("dotnet ef dbcontext scaffold") + ") liest EF das Schema und "
    "erzeugt Entitäten und Context. Ein Plugin, das eigene Schema-Objekte verwaltet, sollte "
    "sie dabei erkennen. Der Weg führt über drei Stationen, von denen das Plugin zwei "
    "bereits kennt:"))
S += flow_diagram([
    ("IDatabaseModelFactory", "Liest das Schema in ein DatabaseModel; das Plugin hängt "
                              "RLS-Daten als Annotations an die Tabellen"),
    ("Scaffolding-Model-Factory", "EF überträgt Tabellen-Annotations auf die Entity-Typen"),
    ("IAnnotationCodeGenerator", "Schreibt sie als HasRowLevelSecurity/HasPolicy (Kapitel 20)"),
])
S += code_block('''
public class TenancyDatabaseModelFactory : NpgsqlDatabaseModelFactory
{
    public TenancyDatabaseModelFactory(
        IDiagnosticsLogger<DbLoggerCategory.Scaffolding> logger)
        : base(logger)
    {
    }

    public override DatabaseModel Create(
        DbConnection dbConnection, DatabaseModelFactoryOptions options)
    {
        var model = base.Create(dbConnection, options);
        // ... Verbindung öffnen, falls nötig ...
        ReadRowLevelSecurity(dbConnection, model);    // pg_class.relrowsecurity / ...force...
        ReadPolicies(dbConnection, model);            // pg_policies
        return model;
    }

    protected virtual void ReadPolicies(DbConnection connection, DatabaseModel model)
    {
        // SELECT schemaname, tablename, policyname, permissive, roles::text[], cmd,
        //        qual, with_check FROM pg_catalog.pg_policies
        // ...
        var policy = new RlsPolicy(
            Name: reader.GetString(2),
            Command: Enum.Parse<PolicyCommand>(reader.GetString(5), ignoreCase: true),
            Permissive: reader.GetString(3) == "PERMISSIVE",
            Roles: roles is ["public"] ? null : roles,    // "public" = Default ohne TO
            Using: reader.IsDBNull(6) ? null : reader.GetString(6),
            WithCheck: reader.IsDBNull(7) ? null : reader.GetString(7));

        table[RlsAnnotationNames.PolicyPrefix + policy.Name] = policy.Serialize();
    }
}
''', label="Scaffolding/TenancyDatabaseModelFactory.cs (gekürzt)")
S.append(p(
    "Registriert wird die Factory wie der Code-Generator in den Design-Time-Services, mit " +
    c("RemoveAll") + " und " + c("AddSingleton") + ". Das Datenformat ist dasselbe wie bei "
    "der Fluent API; deshalb funktionieren Snapshot, Migrations und Code-Generator ohne "
    "weitere Anpassung."))
S += callout("hinweis", [
    "PostgreSQL speichert Policy-Ausdrücke in normalisierter Form, etwa " +
    c("((\"TenantId\" = (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid))") +
    ". Gescaffoldete Policies enthalten genau diesen Text. Das ist korrekt, sieht aber anders "
    "aus als die von der Convention erzeugte Form. Teams, die nach dem Scaffolding auf "
    "Migrations umsteigen, sollten die Policies einmal vereinheitlichen, sonst erzeugt die "
    "erste Migration ein DROP/CREATE mit gleichem Inhalt.",
], title="Normalisierte Ausdrücke")
S += code_block('''
[Fact]
public void Scaffolding_reads_rls_and_policies()
{
    using var ctx = ContextFor(null);
    var factory = new TenancyDatabaseModelFactory(
        ctx.GetService<IDiagnosticsLogger<DbLoggerCategory.Scaffolding>>());

    var model = factory.Create(
        _fixture.OwnerConnectionString, new DatabaseModelFactoryOptions());
    var orders = model.Tables.Single(t => t.Name == "Orders");

    Assert.Equal(true, orders[RlsAnnotationNames.Enabled]);
    Assert.Equal(true, orders[RlsAnnotationNames.Forced]);
}
''', label="tests/...IntegrationTests/AdvancedTests.cs (Auszug)")

# ------------------------------------------------------------ 41
S += h1("Was in den Kern gehört")
S.append(p(
    "Ein zweiter Provider ist der Härtetest für die Paketarchitektur. Er zeigt, welche Teile "
    "wirklich provider-unabhängig sind. Die Faustregel: <b>Was keine SQL-Syntax und keinen "
    "Provider-Typ kennt, gehört in den Kern.</b> Für das Beispiel ergibt sich:"))
S += data_table(
    ["Baustein", "Ort", "Begründung"],
    [
        [c("TenantId") + ", Konverter, JSON-Reader/Writer", "Kern", "Reine .NET-Typen"],
        [c("ITenantOwned") + ", " + c("ITenantProvider") + ", Attribute", "Kern",
         "Für Entitäts-Assemblies ohne Provider-Abhängigkeit"],
        [c("IsTenantProperty()") + ", Kandidatensuche", "Kern", "Nur Metadaten"],
        ["Laufzeit-Annotation, " + c("GetTenantPropertyName"), "Kern",
         "Vom provider-neutralen Interceptor gelesen"],
        ["SaveChanges-Interceptor", "Kern", "Arbeitet nur mit dem Change Tracker"],
        [c("TenantSessionInterceptor") + " (abstrakt)", "Kern",
         "Ablauf gleich, nur der SQL-Befehl unterscheidet sich"],
        ["Evaluatable-Filter mit Attribut", "Kern", "Erkennt Funktionsklassen aller Pakete"],
        ["Value Generation, Logging, " + c("AddAcmeTenancy"), "Kern", "Ohne SQL-Bezug"],
        [c("Database.ApplyTenantSession()"), "Kern", "Nutzt den Interceptor des Providers"],
        ["Type Mapping, Translators, Migrations, RLS-Conventions", "Provider",
         "Store-Typen, SQL-Syntax, Schema-Objekte"],
        ["Eigene SQL-Knoten, Aggregate", "Provider", "Abhängig von Syntax und Provider-Factory"],
    ], [1.9, 0.7, 2.1])
S += callout("wichtig", [
    "Zwei Provider-Pakete, die beide eine Extension-Method " + c("UseAcmeTenancy") + " auf " +
    c("DbContextOptionsBuilder") + " definieren, sind nicht gleichzeitig referenzierbar: Der "
    "Aufruf wäre mehrdeutig. Das PostgreSQL-Paket nutzt den Builder von EF, das SQL-Server-"
    "Paket den Builder des Providers (" + c("UseSqlServer(cs, x => x.UseAcmeTenancy())") +
    "). Wer mehrere Provider-Pakete plant, sollte von Anfang an konsequent den "
    "Provider-Builder verwenden, wie das NetTopologySuite-Plugin.",
], title="Namenskonflikte zwischen Provider-Paketen")

# ------------------------------------------------------------ 42
S += h1("Ein zweiter Provider: SQL Server")
S.append(p(
    "Das Paket " + c("Acme.EntityFrameworkCore.Tenancy.SqlServer") + " setzt dieselbe Idee "
    "mit den Mitteln von SQL Server um. Die Unterschiede zeigen, was ein Plugin pro "
    "Datenbank tatsächlich neu lösen muss:"))
S += data_table(
    ["Aspekt", "PostgreSQL", "SQL Server"],
    [
        ["Mandant in der Session", c("set_config(name, wert, false)"),
         c("sp_set_session_context")],
        [c("CurrentTenant()") + " in SQL", c("nullif(current_setting(...), '')::uuid"),
         c("CAST(SESSION_CONTEXT(N'…') AS uniqueidentifier)")],
        ["Spaltentyp", c("uuid"), c("uniqueidentifier")],
        ["RLS-Mechanismus", c("CREATE POLICY") + " pro Tabelle",
         "Predicate-Funktion + " + c("CREATE SECURITY POLICY")],
        ["Lesen / Schreiben", c("USING") + " / " + c("WITH CHECK"),
         c("FILTER") + " / " + c("BLOCK ... AFTER INSERT/UPDATE")],
        ["Tabelleneigentümer", "Nur mit " + c("FORCE") + " gebunden",
         "Kein FORCE; Prädikate sind Teil der Abfrage"],
        [c("DROP TABLE"), "Policies verschwinden mit", "Policy muss vorher weg (SCHEMABINDING)"],
        ["DDL in Batches", "Transaktional, beliebig kombinierbar",
         c("CREATE FUNCTION/SCHEMA") + " allein im Batch: " + c("EXEC(N'...')")],
    ], [1.3, 1.6, 1.9])
S += h2("Aktivierung über den Provider-Builder")
S += code_block('''
services.AddAcmeTenancy<HttpTenantProvider>();                    // Kernpaket
services.AddDbContext<ShopContext>((sp, o) => o
    .UseSqlServer(connectionString, sql => sql.UseAcmeTenancy(sp)));

public static SqlServerDbContextOptionsBuilder UseAcmeTenancy(
    this SqlServerDbContextOptionsBuilder sqlServerOptionsBuilder,
    string sessionKey = "TenantId",
    string policySchema = "acme")
{
    // Vom Provider-Builder zum eigentlichen DbContextOptionsBuilder
    var optionsBuilder =
        ((IRelationalDbContextOptionsBuilderInfrastructure)sqlServerOptionsBuilder)
        .OptionsBuilder;

    var extension = optionsBuilder.Options.FindExtension<SqlServerTenancyOptionsExtension>()
                    ?? new SqlServerTenancyOptionsExtension();
    // ... Settings validieren und setzen ...
    ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
        .AddOrUpdateExtension(extension.WithSettings(settings));

    return sqlServerOptionsBuilder;
}
''', label="Anwendung und Infrastructure/SqlServerTenancyOptionsBuilderExtensions.cs (gekürzt)")

S += h2("Migrations: Security Policy statt Policy-Ausdruck")
S.append(p(
    "Die Convention vermerkt für SQL Server nur die Mandantenspalte; das SQL entsteht im "
    "Generator. Die Predicate-Funktion ist für alle Tabellen dieselbe und wird idempotent "
    "angelegt:"))
S += code_block('''
protected virtual void CreatePolicy(string table, string? schema, string column,
    MigrationCommandListBuilder builder)
{
    var function = Delimit(PredicateFunctionName, Settings.PolicySchema);
    var target = Delimit(table, schema);
    var predicate = $"{function}({Delimit(column)})";

    // CREATE SCHEMA/FUNCTION müssen allein im Batch stehen -> EXEC(N'...')
    Statement(builder, $"IF SCHEMA_ID({Literal(Settings.PolicySchema)}) IS NULL "
        + $"EXEC({Literal($"CREATE SCHEMA {Delimit(Settings.PolicySchema)}")})");

    var functionSql =
        $"CREATE FUNCTION {function}(@TenantId uniqueidentifier) "
        + "RETURNS TABLE WITH SCHEMABINDING AS RETURN SELECT 1 AS [Allowed] "
        + $"WHERE @TenantId = CAST(SESSION_CONTEXT({Literal(Settings.SessionKey)}) "
        + "AS uniqueidentifier)";
    Statement(builder, $"IF OBJECT_ID({Literal(function)}, N'IF') IS NULL "
        + $"EXEC({Literal(functionSql)})");

    Statement(builder,
        $"CREATE SECURITY POLICY {PolicyName(table)} "
        + $"ADD FILTER PREDICATE {predicate} ON {target}, "
        + $"ADD BLOCK PREDICATE {predicate} ON {target} AFTER INSERT, "
        + $"ADD BLOCK PREDICATE {predicate} ON {target} AFTER UPDATE "
        + "WITH (STATE = ON)");
}

protected override void Generate(DropTableOperation operation, IModel? model,
    MigrationCommandListBuilder builder, bool terminate = true)
{
    DropPolicy(operation.Name, builder);       // SCHEMABINDING: zuerst die Policy
    base.Generate(operation, model, builder, terminate);
}
''', label="Migrations/Internal/SqlServerTenancyMigrationsSqlGenerator.cs (Auszug)")
S.append(p(
    c("SCHEMABINDING") + " akzeptiert nur zweiteilige Namen. Eine Tabelle ohne explizites "
    "Schema qualifizieren die Aufrufer deshalb mit dem Default-Schema des Modells oder, "
    "falls keines gesetzt ist, mit " + c("dbo") + "; sonst lehnt SQL Server die Security "
    "Policy ab."))
S += callout("wichtig", [
    "Das " + c("EXEC(N'...')") + " enthält SQL in einem String-Literal, das wiederum "
    "Literale enthält. Die Verschachtelung entsteht ausschließlich über " +
    c("GenerateSqlLiteral") + " des String-Mappings, das Anführungszeichen korrekt "
    "verdoppelt; Bezeichner laufen über " + c("DelimitIdentifier") + ". Session-Schlüssel "
    "und Schema werden zusätzlich beim Konfigurieren validiert.",
], title="Verschachteltes SQL")

S += h2("Laufzeit: derselbe Ablauf, anderer Befehl")
S += code_block('''
public class SqlServerTenantSessionInterceptor : TenantSessionInterceptor
{
    public SqlServerTenantSessionInterceptor(ITenantProvider tenants)
        : base(tenants)
    {
    }

    protected override DbCommand CreateSessionCommand(
        DbConnection connection, DbContext? context, TenantId? tenant)
    {
        var key = context?.GetService<ISqlServerTenancySingletonOptions>()
                      .Settings.SessionKey
                  ?? SqlServerTenancySettings.Default.SessionKey;

        var command = connection.CreateCommand();
        command.CommandText = "EXEC sp_set_session_context @key = @key, @value = @value";
        AddParameter(command, "@key", key);
        AddParameter(command, "@value", tenant?.Value);    // NULL: keine Zeilen sichtbar
        return command;
    }
}
''', label="Runtime/SqlServerTenantSessionInterceptor.cs")

S += h2("Bewusst nicht portiert")
S.append(p(
    "Das SQL-Server-Paket ist ein vollständiger, aber schlankerer Provider. Fluent-API-"
    "Policies, der optionale Query-Filter, eigene SQL-Knoten, Aggregate, Snapshot-Code-"
    "Generator und Scaffolding fehlen dort. Das ist eine Produktentscheidung, kein "
    "technisches Hindernis: Jedes dieser Kapitel lässt sich mit denselben Mustern auf SQL "
    "Server übertragen. Die Liste gehört in die Dokumentation des Pakets, damit Nutzer "
    "wissen, was sie erwarten können."))
S += code_block('''
[Fact]
public async Task Block_predicate_rejects_rows_of_other_tenant()
{
    await using var db = ContextFor(TenantId.New());

    var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync(
        $"INSERT INTO [Orders] ([TenantId], [Total]) VALUES ({Guid.NewGuid()}, 1)"));

    Assert.Equal(33504, ex.Number);      // Verstoß gegen BLOCK PREDICATE
}
''', label="tests/...SqlServer.Tests/SqlServerTests.cs (Auszug)")
S.append(p(
    "Die Tests laufen wie bei PostgreSQL in zwei Ebenen: ohne Datenbank für Modell, Abfrage- "
    "und Migrations-SQL und mit Testcontainers gegen einen echten SQL Server. Dort verbindet "
    "sich die Anwendung mit einem eigenen Login statt als " + c("sa") + ", um die "
    "Produktionssituation nachzustellen."))
