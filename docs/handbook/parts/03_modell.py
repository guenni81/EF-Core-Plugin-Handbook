# ============================================================ TEIL III
S += part(
    "Das Modell",
    "Metadaten, Fluent API, Conventions, Validierung und neue Spaltentypen.",
    ["Kapitel 7: Annotations und Fluent API",
     "Kapitel 8: Conventions",
     "Kapitel 9: Modellvalidierung",
     "Kapitel 10: Type Mappings und Value Converter"])

# ------------------------------------------------------------ 7
S += h1("Annotations und Fluent API")
S.append(p(
    "Annotations sind EFs Mechanismus für erweiterbare Metadaten: Schlüssel-Wert-Paare an "
    "jedem Modellelement (Modell, Entity-Typ, Property, Index, ...). Ein Plugin speichert "
    "seine Konfiguration als Annotations, und die gesamte restliche Infrastruktur "
    "(Conventions, Migrations, Snapshots, Compiled Models) kann damit arbeiten."))
S += h2("Regeln für Annotations")
S += data_table(
    ["Regel", "Begründung"],
    [
        ["Eigenes Präfix (" + c("Acme:Rls:") + ")",
         "Vermeidet Kollisionen mit EF, Providern und anderen Plugins."],
        ["Nur einfache Werte: string, bool, Zahlen, Enums",
         "Snapshots und Migrationen schreiben Werte als C#-Literale. Eigene Klassen "
         "lassen sich nicht serialisieren."],
        ["Komplexe Werte als String (z. B. JSON)",
         "Strings vergleicht der Differ zuverlässig per " + c("Equals") + ". Ein Record mit "
         + c("string[]") + " würde per Referenz verglichen und ständig „geändert“ wirken."],
        ["Default-Werte nicht speichern",
         "„Annotation fehlt“ und „Annotation hat Default“ sind für den Differ verschieden "
         "und erzeugen leere Migrationen."],
        ["Je Teilkonfiguration eine Annotation",
         "Jede Policy hat eine eigene Annotation. Dann zeigt der Diff genau, welche sich "
         "geändert hat."],
    ], [1.5, 2.5])
S += code_block('''
public static class RlsAnnotationNames
{
    public const string Prefix = "Acme:Rls:";
    public const string Enabled = Prefix + "Enabled";
    public const string Forced = Prefix + "Forced";
    public const string PolicyPrefix = Prefix + "Policy:";     // + Policy-Name
    public const string QueryFilterKey = "Acme.Tenancy";
}

public sealed record RlsPolicy(string Name, PolicyCommand Command, bool Permissive,
    string[]? Roles, string? Using, string? WithCheck)
{
    public string Serialize()
        => JsonSerializer.Serialize(this, RlsJsonContext.Default.RlsPolicy);

    public static RlsPolicy Deserialize(string json) => /* ... */;
}
''', label="Metadata/RlsAnnotationNames.cs, Metadata/RlsPolicy.cs (gekürzt)")

S += h2("Die Fluent API")
S.append(p(
    "Fluent-API-Methoden sind Extension-Methods auf den Buildern von EF. Drei "
    "Gestaltungsregeln machen sie professionell: Die <b>nicht-generische</b> Variante ist die "
    "eigentliche Implementierung (der Model-Snapshot nutzt nicht-generische Builder, "
    "Kapitel 20). Die <b>generische</b> Variante delegiert nur und erhält den Typ für "
    "weiteres Chaining. Und die Klasse liegt im Namespace " + c("Microsoft.EntityFrameworkCore") +
    ", damit sie überall ohne " + c("using") + " verfügbar ist."))
S += code_block('''
namespace Microsoft.EntityFrameworkCore;

public static class RlsEntityTypeBuilderExtensions
{
    public static EntityTypeBuilder HasRowLevelSecurity(
        this EntityTypeBuilder builder, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Metadata.SetAnnotation(RlsAnnotationNames.Enabled, true);

        if (force)
            builder.Metadata.SetAnnotation(RlsAnnotationNames.Forced, true);
        else
            builder.Metadata.RemoveAnnotation(RlsAnnotationNames.Forced);   // kein Default!

        return builder;
    }

    public static EntityTypeBuilder HasPolicy(
        this EntityTypeBuilder builder, string name, Action<PolicyBuilder> configure)
    {
        var policyBuilder = new PolicyBuilder(name);
        configure(policyBuilder);
        builder.Metadata.SetAnnotation(
            RlsAnnotationNames.PolicyPrefix + name, policyBuilder.Build().Serialize());
        return builder;
    }

    public static EntityTypeBuilder<TEntity> HasRowLevelSecurity<TEntity>(
        this EntityTypeBuilder<TEntity> builder, bool force = false) where TEntity : class
        => (EntityTypeBuilder<TEntity>)HasRowLevelSecurity(
            (EntityTypeBuilder)builder, force);

    // ... HasNoRowLevelSecurity, HasPolicy<TEntity> analog
}
''', label="Builders/RlsEntityTypeBuilderExtensions.cs (gekürzt)")
S.append(p(
    "Verschachtelte Konfiguration wie bei " + c("HasPolicy") + " nimmt ein " +
    c("Action<PolicyBuilder>") + " entgegen. Der Builder prüft die Regeln der Datenbank "
    "schon beim Modellbau. Ein Fehler wie „INSERT-Policy mit USING“ fällt so beim ersten "
    "Start der Anwendung auf und nicht erst beim Ausführen der Migration in Produktion:"))
S += code_block('''
internal RlsPolicy Build()
{
    if (_using is null && _withCheck is null)
        throw new InvalidOperationException(
            $"Policy '{_name}' braucht USING oder WITH CHECK.");

    if (_command == PolicyCommand.Insert && _using is not null)
        throw new InvalidOperationException(
            $"INSERT-Policy '{_name}' erlaubt nur WITH CHECK.");

    if (_command is PolicyCommand.Select or PolicyCommand.Delete && _withCheck is not null)
        throw new InvalidOperationException(
            $"{_command}-Policy '{_name}' erlaubt nur USING.");

    return new RlsPolicy(_name, _command, _permissive,
        _roles.Count > 0 ? [.. _roles] : null, _using, _withCheck);
}
''', label="Builders/PolicyBuilder.cs › Build")

S += h2("Konfigurationsquellen")
S.append(p(
    "EF merkt sich zu jedem Metadatum, <i>woher</i> es stammt. Eine Quelle kann eine "
    "gleich starke oder schwächere überschreiben, aber nie eine stärkere. Das ist der "
    "Mechanismus, mit dem Plugins sinnvolle Defaults setzen, die Nutzer jederzeit "
    "übersteuern können:"))
S += data_table(
    ["ConfigurationSource", "Gesetzt durch", "Stärke"],
    [
        [c("Explicit"), "Fluent API im " + c("OnModelCreating") + " (" +
         c("Metadata.SetAnnotation") + ")", "stärkste"],
        [c("DataAnnotation"), "Attribut-Conventions (" + c("fromDataAnnotation: true") + ")", "mittel"],
        [c("Convention"), "Conventions über " + c("IConvention*Builder"), "schwächste"],
    ], [1.2, 2.4, 0.8])
S.append(p(
    "Für eigene Conventions bietet das Plugin deshalb zusätzlich Überladungen auf dem "
    "Convention-Builder an. Sie geben " + c("null") + " zurück, wenn eine stärkere Quelle "
    "anders entschieden hat:"))
S += code_block('''
public static IConventionEntityTypeBuilder? HasRowLevelSecurity(
    this IConventionEntityTypeBuilder builder,
    bool enabled,
    bool fromDataAnnotation = false)
    => builder.HasAnnotation(RlsAnnotationNames.Enabled, enabled, fromDataAnnotation)
           is null
        ? null
        : builder;
''', label="Builders/RlsEntityTypeBuilderExtensions.cs › Convention-Variante")

# ------------------------------------------------------------ 8
S += h1("Conventions")
S.append(p(
    "Conventions verändern das Modell automatisch. EF selbst besteht zum größten Teil aus "
    "Conventions: Schlüssel erkennen, Beziehungen finden, Tabellennamen ableiten. Ein Plugin "
    "fügt eigene hinzu, um Konfiguration überflüssig zu machen."))
S += h2("Convention-Typen und Zeitpunkte")
S.append(p(
    "Eine Convention implementiert ein oder mehrere Interfaces. Jedes steht für ein "
    "Ereignis während des Modellbaus. Die wichtigsten:"))
S += data_table(
    ["Interface", "Ereignis", "Typische Verwendung"],
    [
        [c("IEntityTypeAddedConvention"), "Entity-Typ wurde hinzugefügt",
         "Attribute auswerten, Grundkonfiguration"],
        [c("IPropertyAddedConvention"), "Property wurde hinzugefügt",
         "Spaltentypen, Namenskonventionen"],
        [c("IForeignKeyAddedConvention"), "Beziehung entstanden", "Indizes, Löschverhalten"],
        [c("IEntityTypeBaseTypeChangedConvention"), "Vererbung geändert",
         "Konfiguration verschieben"],
        [c("IModelFinalizingConvention"), "Modell wird abgeschlossen (noch änderbar)",
         "Alles, was vom fertigen Modell abhängt"],
        [c("IModelFinalizedConvention"), "Modell ist fertig (read-only)", "Validierung"],
    ], [1.6, 1.4, 1.6])
S += callout("wichtig", [
    "Reaktive Conventions (" + c("*Added") + ", " + c("*Changed") + ") laufen, während der "
    "Nutzer noch konfiguriert. Ein Spaltenname kann sich danach noch ändern, ein Entity-Typ "
    "kann noch einen Basistyp bekommen. Wer davon abhängt, muss auf <i>alle</i> relevanten "
    "Ereignisse reagieren oder " + c("IModelFinalizingConvention") + " verwenden. Letzteres "
    "ist einfacher und für die meisten Plugins richtig.",
], title="Der häufigste Convention-Fehler")

S += h2("Conventions einhängen")
S.append(p(
    "Das Plugin-Interface " + c("IConventionSetPlugin") + " bekommt das Convention-Set und "
    "fügt eigene Conventions hinzu. " + c("ConventionSet.Add") + " trägt eine Convention in "
    "alle Listen ein, deren Interfaces sie implementiert, jeweils am Ende:"))
S += code_block('''
public class TenancyConventionSetPlugin : IConventionSetPlugin
{
    private readonly ProviderConventionSetBuilderDependencies _dependencies;
    private readonly IDbContextOptions _options;
    private readonly ISqlGenerationHelper _sqlGenerationHelper;
    private readonly TenancyLoggingDefinitions _loggingDefinitions;

    public TenancyConventionSetPlugin(
        ProviderConventionSetBuilderDependencies dependencies,
        IDbContextOptions options,
        ISqlGenerationHelper sqlGenerationHelper,
        TenancyLoggingDefinitions loggingDefinitions)
    {
        // ... ArgumentNullException.ThrowIfNull für alle Parameter ...
        _dependencies = dependencies;
        _options = options;
        _sqlGenerationHelper = sqlGenerationHelper;
        _loggingDefinitions = loggingDefinitions;
    }

    public virtual ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        var settings = _options.FindExtension<TenancyOptionsExtension>()?.Settings;
        if (settings is null)
            return conventionSet;

        conventionSet.Add(new TenantIdKeyValueGenerationConvention());     // Kapitel 28
        conventionSet.Add(new NoTenantIsolationAttributeConvention(_dependencies));
        conventionSet.Add(new TenantIsolationConvention(
            settings, _sqlGenerationHelper, _dependencies.Logger, _loggingDefinitions));
        conventionSet.Add(new TenancyModelValidationConvention(
            _dependencies.Logger, _loggingDefinitions));

        return conventionSet;
    }
}
''', label="Conventions/TenancyConventionSetPlugin.cs")
S.append(p(
    c("ProviderConventionSetBuilderDependencies") + " ist das Parameterobjekt, das auch EFs "
    "eigene Conventions bekommen. Es enthält unter anderem den Model-Logger, den Validation-"
    "Logger und die Type-Mapping-Source. Andere Dienste wie " + c("ISqlGenerationHelper") +
    " lassen sich zusätzlich injizieren, weil das Plugin selbst im internen Provider "
    "aufgelöst wird."))

S += h2("Die Isolations-Convention")
S.append(p(
    "Die zentrale Convention des Beispiels läuft bei " + c("ModelFinalizing") + ", weil "
    "Spaltennamen erst dann feststehen. Sie findet die Mandanten-Property (per " +
    c("IsTenantProperty()") + " markiert oder " + c("TenantId") + " bei " +
    c("ITenantOwned") + ", Kapitel 39), aktiviert RLS mit Policy, legt einen Index an, "
    "setzt eine Laufzeit-Annotation für den SaveChanges-Interceptor und optional einen "
    "Query-Filter:"))
S += code_block('''
public virtual void ProcessModelFinalizing(
    IConventionModelBuilder modelBuilder,
    IConventionContext<IConventionModelBuilder> context)
{
    foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
    {
        if (entityType.BaseType is not null       // nur Root-Typen einer Hierarchie
            || entityType.IsOwned())              // Owned Types teilen die Tabelle
            continue;

        // Markierte Property oder "TenantId" bei ITenantOwned (Kernpaket, Kapitel 39)
        if (entityType.FindTenantPropertyCandidate() is not IConventionProperty property)
            continue;                             // (+ Typprüfung, gekürzt)

        if (entityType.IsRlsOptedOut())
        {
            _logger.TenantIsolationOptedOut(_loggingDefinitions, entityType);  // Warnung
            continue;
        }

        // Keine Tabelle (View, ToSqlQuery, Keyless) -> kein RLS
        if (StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)
            is not { } table)
            continue;

        // Laufzeit-Annotation: der SaveChanges-Interceptor findet so die Property
        entityType.Builder.HasAnnotation(
            TenancyAnnotationNames.TenantProperty, property.Name);

        ApplyRowLevelSecurity(entityType, property, table);
        if (_settings.CreateTenantIndex) EnsureTenantIndex(entityType, property);
        if (_settings.ApplyQueryFilter) ApplyQueryFilter(entityType, property);

        _logger.TenantIsolationApplied(_loggingDefinitions, entityType);       // Debug
    }
}
''', label="Conventions/TenantIsolationConvention.cs › ProcessModelFinalizing")
S += code_block('''
protected virtual void ApplyRowLevelSecurity(
    IConventionEntityType entityType,
    IConventionProperty property,
    StoreObjectIdentifier table)
{
    // Spaltenname für genau diese Tabelle (bei Table Splitting kann er abweichen)
    var column = _sqlGenerationHelper.DelimitIdentifier(property.GetColumnName(table)!);
    var condition =
        $"{column} = nullif(current_setting('{_settings.SettingName}', true), '')::uuid";

    var policy = new RlsPolicy(_settings.PolicyName, PolicyCommand.All, Permissive: true,
        Roles: null, Using: condition, WithCheck: condition);

    // Convention-Builder: überschreibt explizite Konfiguration NICHT
    var builder = entityType.Builder;
    builder.HasAnnotation(RlsAnnotationNames.Enabled, true);
    if (_settings.ForceRowLevelSecurity)
        builder.HasAnnotation(RlsAnnotationNames.Forced, true);
    builder.HasAnnotation(RlsAnnotationNames.PolicyPrefix + _settings.PolicyName,
        policy.Serialize());
}
''', label="Conventions/TenantIsolationConvention.cs › ApplyRowLevelSecurity")
S += callout("wichtig", [
    "Der Setting-Name wird unmaskiert in den Policy-Ausdruck geschrieben. Anders als bei "
    "SQL, das der Entwickler selbst in " + c("Using(...)") + " schreibt, setzt hier das "
    "<i>Plugin</i> SQL zusammen und ist dafür verantwortlich, dass nichts Ungültiges "
    "hineinkommt. Deshalb validiert der Options-Builder den Namen mit einem strengen "
    "regulären Ausdruck (" + c("[a-z_][a-z0-9_]*\\.[a-z_][a-z0-9_]*") + ").",
], title="Selbst erzeugtes SQL validieren")

S += h2("Strukturelle Änderungen: Index und Query-Filter")
S.append(p(
    "Conventions dürfen nicht nur Annotations setzen, sondern das Modell strukturell "
    "verändern. Der Index wird nur angelegt, wenn nicht schon ein Index oder Schlüssel mit "
    "der Mandantenspalte beginnt. Der Query-Filter wird als Expression Tree gebaut und als "
    "<b>benannter Filter</b> registriert, eine Neuerung in EF Core 10. Benannte Filter "
    "koexistieren mit eigenen Filtern des Nutzers und lassen sich gezielt per " +
    c('IgnoreQueryFilters(["Acme.Tenancy"])') + " abschalten."))
S += code_block('''
protected virtual void EnsureTenantIndex(
    IConventionEntityType entityType, IConventionProperty property)
{
    var covered = entityType.GetIndexes().Any(i => i.Properties[0] == property)
                  || entityType.GetKeys().Any(k => k.Properties[0] == property);
    if (!covered)
        entityType.Builder.HasIndex(new[] { property });
}

protected virtual void ApplyQueryFilter(
    IConventionEntityType entityType, IConventionProperty property)
{
    // e => EF.Property<TenantId>(e, "...") == EF.Functions.CurrentTenant()
    // EF.Property funktioniert für jeden Property-Namen und auch für Shadow-Properties
    var parameter = Expression.Parameter(entityType.ClrType, "e");
    var body = Expression.Equal(
        Expression.Call(typeof(EF), nameof(EF.Property), [typeof(TenantId)],
            parameter, Expression.Constant(property.Name)),
        Expression.Call(CurrentTenantMethod, Expression.Property(null, FunctionsProperty)));

    entityType.Builder.HasQueryFilter(
        RlsAnnotationNames.QueryFilterKey, Expression.Lambda(body, parameter));
}
''', label="Conventions/TenantIsolationConvention.cs (Auszug)")
S.append(p(
    "Bemerkenswert ist, dass der Filter die eigene Funktion " +
    c("EF.Functions.CurrentTenant()") + " verwendet. Er wird damit über den Translator aus "
    "Kapitel 12 nach SQL übersetzt. Die Bausteine des Plugins greifen ineinander."))
S += h2("Endgültige Namen, egal wer sie setzt")
S.append(p(
    "Die Isolations-Convention liest den Spaltennamen erst bei " + c("ModelFinalizing") +
    " und nur über " + c("GetColumnName(table)") + ". Namen, die andere Conventions oder "
    "Plugins ändern, stehen zu diesem Zeitpunkt fest. Das Beispiel prüft das gegen "
    "EFCore.NamingConventions, das Tabellen und Spalten in snake_case umbenennt, und zwar "
    "in beiden Aktivierungsreihenfolgen:"))
S += code_block('''
options.UseNpgsql(connectionString).UseAcmeTenancy().UseSnakeCaseNamingConvention();
''', label="Anwendung")
S += code_block('''
ALTER TABLE orders ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON orders AS PERMISSIVE FOR ALL
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
CREATE INDEX ix_orders_tenant_id ON orders (tenant_id);
''', lang="sql", label="Erzeugtes Migrations-SQL (Auszug, tests/.../NamingConventionTests.cs)")
S += callout("tipp", [
    "Ein Plugin, das Namen selbst zusammensetzt (aus " + c("Name") + "-Properties oder per "
    "String-Verkettung), bricht, sobald ein Naming-Plugin aktiv ist. Immer das Modell nach "
    "dem endgültigen Namen fragen, und zwar so spät wie möglich.",
], title="Namen nie selbst zusammensetzen")

S += h2("Attribut-Conventions")
S.append(p(
    "Data Annotations wertet man mit einer Attribut-Convention aus. EF bietet dafür die "
    "Basisklasse " + c("TypeAttributeConventionBase<TAttribute>") + ", die das Attribut "
    "findet und nur noch die Reaktion verlangt. Der Wert wird mit " +
    c("fromDataAnnotation: true") + " gesetzt, sodass die Fluent API ihn überstimmen kann:"))
S += code_block('''
public class NoTenantIsolationAttributeConvention
    : TypeAttributeConventionBase<NoTenantIsolationAttribute>
{
    public NoTenantIsolationAttributeConvention(
        ProviderConventionSetBuilderDependencies dependencies)
        : base(dependencies)
    {
    }

    protected override void ProcessEntityTypeAdded(
        IConventionEntityTypeBuilder entityTypeBuilder,
        NoTenantIsolationAttribute attribute,
        IConventionContext<IConventionEntityTypeBuilder> context)
        => entityTypeBuilder.HasRowLevelSecurity(enabled: false, fromDataAnnotation: true);
}
''', label="Conventions/NoTenantIsolationAttributeConvention.cs")
S += callout("tipp", [
    "Das Attribut selbst liegt im <i>Kernpaket</i>, die Convention im Provider-Paket. "
    "Entitätsklassen können das Attribut verwenden, ohne von PostgreSQL abzuhängen. "
    "Das ist dieselbe Aufteilung wie bei EFs eigenen Attributen in "
    + c("System.ComponentModel.DataAnnotations") + ".",
])

# ------------------------------------------------------------ 9
S += h1("Modellvalidierung")
S.append(p(
    "Ein Plugin sollte Fehlkonfiguration erkennen, bevor sie zu falschem SQL oder "
    "Datenlecks führt. EF validiert Modelle über " + c("IModelValidator") + ", den es aber "
    "nur einmal gibt und der dem Provider gehört. Für Plugins ist eine " +
    c("IModelFinalizedConvention") + " der bessere Ort: Sie läuft auf dem fertigen Modell "
    "und kollidiert mit niemandem."))
S += code_block('''
public virtual IModel ProcessModelFinalized(IModel model)
{
    foreach (var entityType in model.GetEntityTypes())
    {
        if (!entityType.HasRlsConfiguration())
            continue;

        if (entityType.BaseType is not null)
            throw new InvalidOperationException(
                $"RLS an '{entityType.DisplayName()}' ist ungültig: nur am Root-Typ "
                + $"('{entityType.GetRootType().DisplayName()}') erlaubt.");

        if (StoreObjectIdentifier.Create(entityType, StoreObjectType.Table) is null)
            throw new InvalidOperationException("... nicht auf eine Tabelle gemappt ...");

        if (!entityType.IsRlsEnabled() && entityType.GetRlsPolicies().Count > 0)
            _logger.PoliciesWithoutRowLevelSecurity(_loggingDefinitions, entityType);
    }
    return model;
}
''', label="Conventions/TenancyModelValidationConvention.cs")
S.append(p(
    "Die Validierung unterscheidet zwei Arten von Problemen. <b>Strukturelle Fehler</b>, "
    "die garantiert zu falschem Verhalten führen, werfen eine Exception. <b>Fragwürdige "
    "Konfiguration</b>, die gewollt sein kann, erzeugt eine Warnung über das "
    "Logging-System. Nutzer können solche Warnungen gezielt in Fehler umwandeln "
    "(Kapitel 16)."))

# ------------------------------------------------------------ 10
S += h1("Type Mappings und Value Converter")
S.append(p(
    "Ein Type Mapping verbindet einen CLR-Typ mit einem Datenbanktyp. Es bestimmt den "
    "Spaltentyp in Migrationen, wie Parameter gesetzt werden, wie Literale im SQL aussehen, "
    "wie Werte verglichen und kopiert werden und wie sie in JSON-Spalten gespeichert "
    "werden. Für eigene Typen hat ein Plugin zwei Möglichkeiten:"))
S += data_table(
    ["Weg", "Vorteile", "Nachteile"],
    [
        ["Value Converter (Nutzer konfiguriert " + c("HasConversion") + ")",
         "Kein Plugin nötig", "Muss für jede Property oder per " +
         c("ConfigureConventions") + " konfiguriert werden"],
        [c("IRelationalTypeMappingSourcePlugin"),
         "Typ funktioniert überall ohne Konfiguration, eigene Literale, volle Kontrolle",
         "Provider-spezifisch (Store-Typ)"],
    ], [1.5, 1.7, 1.7])

S += h2("Der Typ und sein Konverter")
S.append(p(
    "Der stark typisierte " + c("TenantId") + " und sein Konverter liegen im Kernpaket. "
    "Der Konverter hat eine gemeinsame Instanz; warum, zeigt der Abschnitt über Compiled "
    "Models."))
S += code_block('''
public readonly record struct TenantId(Guid Value)
{
    public static TenantId Empty => default;
    public bool IsEmpty => Value == Guid.Empty;              // wird nach SQL übersetzt
    public static TenantId New() => new(Guid.CreateVersion7());
}

public sealed class TenantIdConverter : ValueConverter<TenantId, Guid>
{
    public static TenantIdConverter Instance { get; } = new();

    public TenantIdConverter()
        : base(id => id.Value, value => new TenantId(value))
    {
    }
}
''', label="TenantId.cs, TenantIdConverter.cs (gekürzt)")

S += h2("Das Type Mapping")
S += code_block('''
public class TenantIdTypeMapping : RelationalTypeMapping
{
    // Pflicht für Compiled Models: öffentliche statische Default-Property
    public static TenantIdTypeMapping Default { get; } = new();

    public TenantIdTypeMapping()
        : base(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(
                typeof(TenantId),
                converter: TenantIdConverter.Instance,
                jsonValueReaderWriter: TenantIdJsonReaderWriter.Instance),
            storeType: "uuid",
            dbType: System.Data.DbType.Guid))
    {
    }

    protected TenantIdTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    // EF klont Mappings, z. B. für Facetten oder geänderte Comparer
    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters)
        => new TenantIdTypeMapping(parameters);

    protected override string GenerateNonNullSqlLiteral(object value)
    {
        var guid = value switch
        {
            TenantId id => id.Value,
            Guid g => g,
            _ => throw new ArgumentException($"Unerwarteter Typ {value.GetType()}."),
        };
        return $"'{guid}'::uuid";
    }
}
''', label="Storage/TenantIdTypeMapping.cs")
S.append(p(
    "Jedes eigene Mapping braucht den geschützten Konstruktor mit " +
    c("RelationalTypeMappingParameters") + " und eine " + c("Clone") + "-Überschreibung, "
    "die den eigenen Typ erzeugt. Fehlt sie, verliert ein geklontes Mapping sein "
    "Verhalten, zum Beispiel die Literal-Erzeugung."))

S += h2("Das Type-Mapping-Plugin")
S.append(p(
    c("RelationalTypeMappingSource") + " befragt alle Plugins, <i>bevor</i> es auf die "
    "Mappings des Providers zurückgreift. Ein Plugin gibt " + c("null") + " zurück, wenn es "
    "nicht zuständig ist:"))
S += code_block('''
public class TenancyTypeMappingSourcePlugin : IRelationalTypeMappingSourcePlugin
{
    public virtual RelationalTypeMapping? FindMapping(
        in RelationalTypeMappingInfo mappingInfo)
    {
        if (mappingInfo.ClrType != typeof(TenantId))
            return null;

        // Explizit anderer Spaltentyp (HasColumnType("text")): nicht zuständig
        var storeTypeBase = mappingInfo.StoreTypeNameBase;
        if (storeTypeBase is not null
            && !string.Equals(storeTypeBase, "uuid", StringComparison.OrdinalIgnoreCase))
            return null;

        return TenantIdTypeMapping.Default;
    }
}
''', label="Storage/TenancyTypeMappingSourcePlugin.cs")
S += callout("hinweis", [
    "Ohne Mapping würde EF " + c("TenantId") + " beim Modellbau nicht als Skalartyp "
    "erkennen und mit einer Fehlermeldung abbrechen. Mit dem Plugin funktioniert der Typ "
    "als Property, als Schlüssel, in Abfragen, als Parameter und in JSON-Spalten, ohne "
    "eine Zeile Konfiguration.",
])

S += h2("JSON-Unterstützung")
S.append(p(
    "Für JSON-Spalten braucht ein Mapping einen " + c("JsonValueReaderWriter") + ". Er hat "
    "drei Pflichten: lesen, schreiben und beschreiben, wie er im generierten Code erzeugt "
    "wird. Das Muster folgt EFs eigenem " + c("JsonGuidReaderWriter") + ":"))
S += code_block('''
public sealed class TenantIdJsonReaderWriter : JsonValueReaderWriter<TenantId>
{
    private static readonly PropertyInfo InstanceProperty =
        typeof(TenantIdJsonReaderWriter).GetProperty(nameof(Instance))!;

    public static TenantIdJsonReaderWriter Instance { get; } = new();

    private TenantIdJsonReaderWriter()
    {
    }

    public override TenantId FromJsonTyped(
        ref Utf8JsonReaderManager manager, object? existingObject = null)
        => new(manager.CurrentReader.GetGuid());

    public override void ToJsonTyped(Utf8JsonWriter writer, TenantId value)
        => writer.WriteStringValue(value.Value);

    // Für Compiled Models und vorkompilierte Abfragen
    public override Expression ConstructorExpression
        => Expression.Property(null, InstanceProperty);
}
''', label="TenantIdJsonReaderWriter.cs")
S += callout("wichtig", [
    "Beim Generieren für NativeAOT (" + c("dotnet ef dbcontext optimize --nativeaot") +
    ") erzeugt der Generator für Compiled Models (Kapitel 21) für jedes Type Mapping Code "
    "der Form " + c("TenantIdTypeMapping.Default.Clone(...)") + ". Er verlangt eine "
    "<b>öffentliche, statische Property</b> namens " + c("Default") + " auf einem "
    "<b>öffentlichen</b> Typ und bricht sonst ab. Konverter und Reader/Writer übernimmt er "
    "nur dann ohne zusätzlichen Code, wenn sie <i>dieselbe Instanz</i> wie im Default-Mapping "
    "sind. Deshalb nutzt das Mapping die statischen Instanzen statt " + c("new ...()") + ". "
    "Ohne NativeAOT löst EF 10 Type Mappings stattdessen beim Start auf; ein Plugin sollte "
    "die Anforderungen trotzdem erfüllen, denn über NativeAOT entscheiden die Anwender, "
    "nicht das Plugin.",
], title="Compiled Models brauchen Default und Instance")
