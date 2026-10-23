# ============================================================ TEIL IV
S += part(
    "Abfragen",
    "Wie LINQ zu SQL wird und wie ein Plugin eigene Funktionen und Member übersetzt.",
    ["Kapitel 11: Die Query-Pipeline",
     "Kapitel 12: Methoden und Member übersetzen",
     "Kapitel 13: Clientseitige Auswertung verhindern",
     "Kapitel 14: Weitere Erweiterungspunkte der Abfrage"])

# ------------------------------------------------------------ 11
S += h1("Die Query-Pipeline")
S.append(p(
    "Eine LINQ-Abfrage durchläuft in EF Core mehrere Stufen, bevor SQL entsteht. Wer weiß, "
    "wo welche Stufe sitzt, weiß auch, wo ein Plugin ansetzen muss:"))
S += flow_diagram([
    ("Funcletizer", "Wertet Teilausdrücke ohne Datenbezug in .NET aus und macht Parameter "
                    "daraus. Plugin: IEvaluatableExpressionFilterPlugin"),
    ("Preprocessing", "Navigationen expandieren, Query-Filter einfügen, normalisieren. "
                      "Plugin: IQueryExpressionInterceptor"),
    ("Queryable-Übersetzung", "Where, Select, OrderBy usw. werden zu einer SelectExpression"),
    ("SQL-Übersetzung", "Ausdrücke werden zu SqlExpressions. Plugins: Method-, Member- "
                        "und Aggregate-Translators"),
    ("Postprocessing", "Nullability-Optimierung, Parameter-abhängige Anpassungen"),
    ("SQL-Generierung", "QuerySqlGenerator schreibt den SQL-Text"),
    ("Materialisierung", "Zeilen werden zu Objekten. Plugin: IMaterializationInterceptor"),
])
S.append(p(
    "Die kompilierte Abfrage wird gecacht. Translators laufen also nur einmal pro "
    "Abfrageform, nicht bei jeder Ausführung. Werte, die sich pro Ausführung ändern "
    "(Parameter, Session-Settings), müssen deshalb im SQL als Parameter oder als "
    "Datenbankfunktion stehen, nie als zur Übersetzungszeit eingesetzte Konstante. Genau "
    "deshalb übersetzt das Beispiel " + c("CurrentTenant()") + " in einen Aufruf von " +
    c("current_setting(...)") + " und setzt nicht den aktuellen Mandanten ein."))

# ------------------------------------------------------------ 12
S += h1("Methoden und Member übersetzen")
S += h2("Das EF.Functions-Muster")
S.append(p(
    "Datenbankfunktionen, die keine Entsprechung in .NET haben, werden als Extension-"
    "Methods auf " + c("DbFunctions") + " deklariert. Sie werden nie ausgeführt; der Körper "
    "wirft nur eine Exception für den Fall, dass jemand sie außerhalb von LINQ aufruft. Die "
    "Deklaration liegt im Kernpaket, die Übersetzung im Provider-Paket:"))
S += code_block('''
namespace Microsoft.EntityFrameworkCore;

public static class TenancyDbFunctionsExtensions
{
    public static TenantId CurrentTenant(this DbFunctions _)
        => throw new InvalidOperationException(
            $"{nameof(CurrentTenant)} ist nur in LINQ-to-Entities-Abfragen verwendbar.");
}
''', label="TenancyDbFunctionsExtensions.cs (Kernpaket)")

S += h2("Ein Method-Call-Translator")
S.append(p(
    "Ein " + c("IMethodCallTranslator") + " bekommt jeden Methodenaufruf, den EF nicht "
    "selbst kennt. Er prüft, ob er zuständig ist, und baut dann aus " +
    c("ISqlExpressionFactory") + " einen SQL-Ausdrucksbaum:"))
S += code_block('''
public class CurrentTenantTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo CurrentTenantMethod =
        typeof(TenancyDbFunctionsExtensions).GetRuntimeMethod(
            nameof(TenancyDbFunctionsExtensions.CurrentTenant), [typeof(DbFunctions)])!;

    private readonly ISqlExpressionFactory _sql;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly ITenancySingletonOptions _options;

    public CurrentTenantTranslator(
        ISqlExpressionFactory sql,
        IRelationalTypeMappingSource typeMappingSource,
        ITenancySingletonOptions options)
    {
        // ... ArgumentNullException.ThrowIfNull für alle Parameter ...
        _sql = sql;
        _typeMappingSource = typeMappingSource;
        _options = options;
    }

    public virtual SqlExpression? Translate(
        SqlExpression? instance, MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method != CurrentTenantMethod)
            return null;                          // nicht zuständig -> nächster Translator

        var text = _typeMappingSource.FindMapping(typeof(string))!;
        var boolean = _typeMappingSource.FindMapping(typeof(bool))!;

        var currentSetting = _sql.Function(
            "current_setting",
            [
                _sql.Constant(_options.Settings.SettingName, text),
                _sql.Constant(true, boolean),
            ],
            nullable: true,
            argumentsPropagateNullability: [true, false],
            typeof(string), text);

        var nullIfEmpty = _sql.Function(
            "nullif",
            [currentSetting, _sql.Constant(string.Empty, text)],
            nullable: true,
            argumentsPropagateNullability: [true, false],
            typeof(string), text);

        return _sql.Convert(nullIfEmpty, typeof(TenantId), TenantIdTypeMapping.Default);
    }
}
''', label="Query/CurrentTenantTranslator.cs")
S.append(p("Aus " + c("o.TenantId == EF.Functions.CurrentTenant()") + " wird damit:"))
S += code_block('''
WHERE o."TenantId" = CAST(nullif(current_setting('app.tenant_id', TRUE), '') AS uuid)
''', lang="sql", label="Erzeugtes SQL (Ausschnitt)")
S += data_table(
    ["Detail", "Bedeutung"],
    [
        [c("return null"), "Vertrag für „nicht zuständig“. Niemals eine Exception für fremde "
         "Methoden werfen; dann ist der nächste Translator an der Reihe."],
        [c("arguments"), "Bei Extension-Methods ist " + c("arguments[0]") + " der " +
         c("this") + "-Parameter (hier " + c("DbFunctions") + ")."],
        [c("nullable"), "Kann das Ergebnis NULL sein? EF optimiert damit " +
         c("IS NULL") + "-Prüfungen. Falsche Angaben führen zu falschen Ergebnissen."],
        [c("argumentsPropagateNullability"), "Wird das Ergebnis NULL, wenn dieses Argument "
         "NULL ist? Für " + c("nullif(x, '')") + ": ja für x, nein für den Leerstring."],
        ["Type Mappings", "Jeder SQL-Ausdruck braucht ein Mapping. Es bestimmt Literale, "
         "Parameter und Casts. " + c("Convert") + " mit dem TenantId-Mapping erzeugt " +
         c("CAST(... AS uuid)") + "."],
        [c("Constant(...)"), "Konstanten werden als Literal über das Mapping erzeugt "
         "und korrekt maskiert, auch der Setting-Name."],
    ], [1.3, 3])

S += h2("Ein Member-Translator")
S.append(p(
    "Für Properties und Felder gibt es " + c("IMemberTranslator") + ". Das Beispiel übersetzt " +
    c("tenantId.IsEmpty") + ". Die Konstante bekommt das Type Mapping der Spalte, damit das "
    "Literal im richtigen Format entsteht:"))
S += code_block('''
public class TenantIdMemberTranslator : IMemberTranslator
{
    private static readonly PropertyInfo IsEmptyProperty =
        typeof(TenantId).GetProperty(nameof(TenantId.IsEmpty))!;

    private readonly ISqlExpressionFactory _sql;

    public TenantIdMemberTranslator(ISqlExpressionFactory sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        _sql = sql;
    }

    public virtual SqlExpression? Translate(
        SqlExpression? instance, MemberInfo member, Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is null || !Equals(member, IsEmptyProperty))
            return null;

        return _sql.Equal(instance, _sql.Constant(TenantId.Empty, instance.TypeMapping));
    }
}
''', label="Query/TenantIdMemberTranslator.cs")

S += h2("Translator-Plugins")
S.append(p(
    "Translators werden nicht direkt registriert, sondern über Plugin-Klassen, die eine "
    "Liste liefern. Die Plugins sind <b>Scoped</b> (Anhang A) und können daher Scoped-Dienste "
    "wie " + c("ISqlExpressionFactory") + " injizieren:"))
S += code_block('''
public class TenancyMethodCallTranslatorPlugin : IMethodCallTranslatorPlugin
{
    public TenancyMethodCallTranslatorPlugin(
        ISqlExpressionFactory sql,
        IRelationalTypeMappingSource typeMappingSource,
        ITenancySingletonOptions options)
    {
        Translators = [new CurrentTenantTranslator(sql, typeMappingSource, options)];
    }

    public virtual IEnumerable<IMethodCallTranslator> Translators { get; }
}

public class TenancyMemberTranslatorPlugin : IMemberTranslatorPlugin
{
    public TenancyMemberTranslatorPlugin(ISqlExpressionFactory sql)
    {
        Translators = [new TenantIdMemberTranslator(sql)];
    }

    public virtual IEnumerable<IMemberTranslator> Translators { get; }
}
''', label="Query/TenancyTranslatorPlugins.cs")
S += callout("tipp", [
    c("query.ToQueryString()") + " erzeugt das SQL einer Abfrage, ohne sie auszuführen und "
    "ohne Datenbankverbindung. Es ist das wichtigste Werkzeug, um Translators zu entwickeln "
    "und zu testen (Kapitel 23).",
])

# ------------------------------------------------------------ 13
S += h1("Clientseitige Auswertung verhindern")
S.append(p(
    "Der erste Schritt der Pipeline, der Funcletizer, wertet jeden Teilausdruck vorab in "
    ".NET aus, der keinen Bezug zu Abfragedaten hat. Aus " + c("o.Total > limit * 2") +
    " wird so ein Parameter. Für " + c("EF.Functions.CurrentTenant()") + " wäre das fatal: "
    "Der Ausdruck hat keinen Datenbezug, also würde EF die Methode ausführen, und die "
    "wirft ihre Exception."))
S.append(p(
    "EF kennt nur wenige Ausnahmen. Im Quellcode von " + c("EvaluatableExpressionFilter") +
    " (EF Core 10) sind das " + c("DateTime.Now") + ", " + c("DateTime.UtcNow") + ", " +
    c("DateTime.Today") + ", die " + c("DateTimeOffset") + "-Gegenstücke, " +
    c("Guid.NewGuid()") + ", " + c("Random.Next(...)") + " und alle Methoden, die in EFs "
    "eigener Klasse " + c("DbFunctionsExtensions") + " deklariert sind. Eigene "
    "EF.Functions-Methoden liegen in einer anderen Klasse und fallen <b>nicht</b> darunter. "
    "Danach fragt EF alle registrierten Plugins:"))
S += code_block('''
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TenancyDbFunctionsAttribute : Attribute;

public class TenancyEvaluatableExpressionFilterPlugin : IEvaluatableExpressionFilterPlugin
{
    public virtual bool IsEvaluatableExpression(Expression expression)
        => expression is not MethodCallExpression { Method.DeclaringType: { } type }
           || !type.IsDefined(typeof(TenancyDbFunctionsAttribute), inherit: false);
}

[TenancyDbFunctions]
public static class TenancyDbFunctionsExtensions { /* CurrentTenant() */ }
''', label="Kernpaket: TenancyDbFunctionsAttribute.cs, Query/TenancyEvaluatable…Plugin.cs")
S.append(p(
    "Das Plugin liegt im <b>Kernpaket</b> und erkennt Funktionsklassen an einem Attribut. "
    "So deckt ein einziges Plugin auch die PostgreSQL-spezifischen Funktionen aus Kapitel "
    "37 und 38 und jedes künftige Provider-Paket ab, ohne dass der Kern diese Klassen "
    "kennen muss."))
S += callout("wichtig", [
    "Ob eine eigene EF.Functions-Methode dieses Plugin braucht, hängt davon ab, ob sie "
    "Argumente mit Datenbezug hat. " + c("EF.Functions.Soundex(c.Name)") + " ist "
    "unproblematisch, weil " + c("c.Name") + " auf eine Spalte zeigt. Methoden ohne "
    "Argumente oder nur mit Konstanten (" + c("CurrentTenant()") + ", " +
    c("Random()") + ", " + c("ServerTime()") + ") brauchen es immer. Fehlt es, kommt die "
    "Exception aus dem Methodenkörper, was schwer zu diagnostizieren ist.",
], title="Wann das Plugin nötig ist")

# ------------------------------------------------------------ 14
S += h1("Weitere Erweiterungspunkte der Abfrage")
S.append(p(
    "Das Beispiel braucht Method- und Member-Translators. Komplexe Plugins greifen auf "
    "weitere Punkte zurück; hier ein Überblick mit Einordnung:"))
S += data_table(
    ["Erweiterungspunkt", "Zweck", "Hinweise"],
    [
        [c("IAggregateMethodCallTranslatorPlugin"), "Eigene Aggregatfunktionen in " +
         c("GroupBy") + " (z. B. " + c("bool_and") + ", Statistikfunktionen)",
         "Scoped, mehrfach registrierbar; umgesetzt in Kapitel 38"],
        [c("IQueryExpressionInterceptor"), "LINQ-Ausdrucksbaum vor der Übersetzung "
         "umschreiben", "Singleton-Interceptor; ideal für Query-Rewrites, Tags, Hints"],
        [c("IMaterializationInterceptor"), "Objekte bei der Materialisierung erzeugen, "
         "initialisieren oder prüfen", "Singleton-Interceptor; muss zustandslos und "
         "thread-sicher sein"],
        [c("IMethodCallTranslatorProvider") + " u. a.", "Translator-Infrastruktur ersetzen",
         "Einzeldienste des Providers; nur in Ausnahmefällen"],
        [c("IQuerySqlGeneratorFactory"), "SQL-Generierung für eigene SqlExpression-Typen",
         "Singleton, Provider-Dienst; umgesetzt in Kapitel 37"],
        [c("IRelationalParameterBasedSqlProcessorFactory"), "Nullability-Verarbeitung "
         "eigener Ausdrucksknoten", "Scoped, Provider-Dienst; umgesetzt in Kapitel 37"],
        [c("IQueryTranslationPostprocessorFactory"), "Ausdrucksbaum nach der Übersetzung "
         "anpassen", "Provider-Dienst; tiefer Eingriff, eng an EF-Interna gebunden"],
    ], [1.6, 1.8, 1.6])
S += callout("hinweis", [
    "Mit NativeAOT und vorkompilierten Abfragen (seit EF Core 9) übersetzt EF Abfragen "
    "bereits beim Build. Translators laufen dann im Build-Prozess, nicht zur Laufzeit. "
    "Sie dürfen deshalb nur von Options und Modell abhängen, nie von Laufzeitzustand. "
    "Das Beispiel erfüllt das: Der Setting-Name ist eine Option, der Mandant selbst wird "
    "erst in der Datenbank-Session ausgewertet.",
], title="Vorkompilierte Abfragen")
