# ============================================================ TEIL II
S += part(
    "Infrastruktur",
    "Wie ein Plugin konfiguriert wird, seine Dienste registriert und sich mit dem "
    "DI-Container der Anwendung verbindet.",
    ["Kapitel 4: Die Options-Extension",
     "Kapitel 5: Dienste registrieren",
     "Kapitel 6: Öffentliche API und Integration in die Anwendung"])

# ------------------------------------------------------------ 4
S += h1("Die Options-Extension")
S.append(p(
    "Der Einstiegspunkt jedes echten Plugins ist eine Klasse, die " +
    c("IDbContextOptionsExtension") + " implementiert. Sie hat drei Aufgaben: Sie trägt die "
    "Konfiguration, sie registriert in " + c("ApplyServices") + " die Dienste im internen "
    "Provider, und sie beschreibt sich über " + c("Info") + " gegenüber dem Cache."))
S += h2("Konfiguration als Record")
S.append(p(
    "Alle modellrelevanten Einstellungen des Beispiels liegen in einem Record. Records "
    "liefern " + c("Equals") + " und " + c("GetHashCode") + " über alle Felder, was gleich "
    "für den Cache-Schlüssel gebraucht wird. Neue Einstellungen fügt man dem Record hinzu, "
    "ohne Hash und Vergleich anfassen zu müssen."))
S += code_block('''
public sealed record TenancySettings
{
    public static TenancySettings Default { get; } = new();

    public string SettingName { get; init; } = "app.tenant_id";
    public string PolicyName { get; init; } = "tenant_isolation";
    public bool ForceRowLevelSecurity { get; init; } = true;
    public bool ApplyQueryFilter { get; init; }
    public bool CreateTenantIndex { get; init; } = true;
}
''', label="Infrastructure/TenancySettings.cs")

S += h2("Immutabilität")
S.append(p(
    "EF vergleicht und cached Options. Eine Extension darf sich nach dem Hinzufügen "
    "deshalb nie mehr ändern; jede Änderung erzeugt eine Kopie. Das Muster aus EF Core "
    "selbst: ein geschützter Kopierkonstruktor und eine virtuelle " + c("Clone") +
    "-Methode, damit abgeleitete Extensions ihren eigenen Typ klonen."))
S += code_block('''
public class TenancyOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public TenancyOptionsExtension()
    {
    }

    protected TenancyOptionsExtension(TenancyOptionsExtension copyFrom)
    {
        ArgumentNullException.ThrowIfNull(copyFrom);

        Settings = copyFrom.Settings;
    }

    public virtual TenancySettings Settings { get; private set; } = TenancySettings.Default;

    public virtual DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    protected virtual TenancyOptionsExtension Clone() => new(this);

    public virtual TenancyOptionsExtension WithSettings(TenancySettings settings)
    {
        var clone = Clone();
        clone.Settings = settings;
        return clone;
    }
''', label="Infrastructure/TenancyOptionsExtension.cs (Auszug)")

S += h2("Die Info-Klasse und der Service-Provider-Cache")
S.append(p(
    "Die Info-Klasse entscheidet, ob EF einen gecachten internen Service Provider "
    "wiederverwenden darf. Dafür gibt es zwei Methoden, die zusammenpassen müssen: " +
    c("GetServiceProviderHashCode") + " als schneller Vorfilter und " +
    c("ShouldUseSameServiceProvider") + " als exakter Vergleich."))
S += code_block('''
private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
{
    public ExtensionInfo(TenancyOptionsExtension extension)
        : base(extension)
    {
    }

    private new TenancyOptionsExtension Extension => (TenancyOptionsExtension)base.Extension;

    public override bool IsDatabaseProvider => false;

    public override string LogFragment
        => $"AcmeTenancy(Setting={Extension.Settings.SettingName}) ";

    public override int GetServiceProviderHashCode() => Extension.Settings.GetHashCode();

    public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        => other is ExtensionInfo o && o.Extension.Settings == Extension.Settings;

    public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
    {
        debugInfo["Acme:Tenancy:Force"] =
            Extension.Settings.ForceRowLevelSecurity.ToString();
        // ... weitere Einstellungen
    }
}
''', label="Infrastructure/TenancyOptionsExtension.cs › ExtensionInfo")
S.append(p("Bei diesen beiden Methoden gibt es zwei klassische Fehler mit gegensätzlichen Folgen:"))
S += data_table(
    ["Fehler", "Folge"],
    [
        ["Modellrelevante Option fehlt im Hash",
         "Zwei Contexts mit unterschiedlicher Konfiguration teilen sich Provider und Modell. "
         "Der zweite bekommt das gecachte, <b>falsche</b> Modell. Fällt im Test oft nicht "
         "auf, weil dort nur eine Konfiguration läuft."],
        ["Pro Context variierender Wert im Hash (Mandant, Connection-String, Instanz)",
         "Für jeden Context entsteht ein neuer interner Provider samt Modell. Speicher und "
         "Startzeit explodieren; EF warnt nach 20 Providern mit " +
         c("ManyServiceProvidersCreatedWarning") + "."],
    ], [1.3, 2.7])
S += callout("wichtig", [
    "In den Hash gehört genau das, was <b>beeinflusst, welche Dienste registriert werden "
    "oder wie das Modell aussieht</b>. Alles andere gehört nicht hinein. Der Mandant selbst "
    "ändert sich pro Request und darf deshalb nie in den Options landen; er kommt zur "
    "Laufzeit über einen Interceptor (Kapitel 15).",
], title="Die Faustregel für den Hash")
S.append(p(
    c("LogFragment") + " erscheint in EFs Startmeldung („initialized using provider … with "
    "options: …“) und hilft bei der Fehlersuche. " + c("PopulateDebugInfo") + " liefert "
    "Werte für EFs Diagnose, wenn zu viele Service Provider entstehen. " +
    c("IsDatabaseProvider") + " ist nur für echte Provider " + c("true") + "."))

S += h2("Validierung")
S.append(p(
    c("Validate") + " wird beim Erstellen des Service Providers aufgerufen. Hier prüft das "
    "Plugin Voraussetzungen, die sich aus der Kombination von Extensions ergeben, und "
    "meldet sie mit einer verständlichen Fehlermeldung statt eines späteren "
    "Folgefehlers:"))
S += code_block('''
public virtual void Validate(IDbContextOptions options)
{
    if (options.FindExtension<NpgsqlOptionsExtension>() is null)
        throw new InvalidOperationException(
            "UseAcmeTenancy() erfordert UseNpgsql(). Für andere Datenbanken wird ein "
            + "eigenes Provider-Paket benötigt.");
}
''', label="Infrastructure/TenancyOptionsExtension.cs › Validate")

# ------------------------------------------------------------ 5
S += h1("Dienste registrieren")
S.append(p(
    c("ApplyServices") + " füllt den internen Service Provider. EF stellt dafür einen "
    "Builder bereit, der die Lebensdauer und Kardinalität jedes EF-Dienstes kennt: " +
    c("EntityFrameworkServicesBuilder") + " für Kerndienste und " +
    c("EntityFrameworkRelationalServicesBuilder") + " zusätzlich für relationale Dienste. "
    "Er registriert jeden Dienst mit der richtigen Lebensdauer und nutzt für mehrfach "
    "registrierbare Dienste automatisch " + c("TryAddEnumerable") + "."))
S += code_block('''
public virtual void ApplyServices(IServiceCollection services)
{
    // 1) Plugin-Interfaces: mehrfach registrierbar, kombinierbar mit anderen Plugins
    new EntityFrameworkRelationalServicesBuilder(services)
        .TryAdd<IRelationalTypeMappingSourcePlugin, TenancyTypeMappingSourcePlugin>()
        .TryAdd<IMethodCallTranslatorPlugin, TenancyMethodCallTranslatorPlugin>()
        .TryAdd<IMemberTranslatorPlugin, TenancyMemberTranslatorPlugin>()
        .TryAdd<IEvaluatableExpressionFilterPlugin,
                TenancyEvaluatableExpressionFilterPlugin>()
        .TryAdd<IConventionSetPlugin, TenancyConventionSetPlugin>()
        .TryAdd<ISingletonOptions, ITenancySingletonOptions>(
            p => p.GetRequiredService<ITenancySingletonOptions>())
        // 2) Eigene Dienste, die EF nicht kennt
        .TryAddProviderSpecificServices(m => m
            .TryAddSingleton<ITenancySingletonOptions, TenancySingletonOptions>()
            .TryAddSingleton<TenancyLoggingDefinitions, TenancyLoggingDefinitions>());

    // 3) Einzeldienste des Providers ersetzen
    services.Replace(ServiceDescriptor
        .Scoped<IMigrationsSqlGenerator, TenancyMigrationsSqlGenerator>());
    services.Replace(ServiceDescriptor
        .Singleton<IRelationalAnnotationProvider, TenancyAnnotationProvider>());
}
''', label="Infrastructure/TenancyOptionsExtension.cs › ApplyServices")
S += data_table(
    ["Methode", "Wann", "Warum"],
    [
        [c("TryAdd<TService, TImpl>()"), "EF-Dienste und Plugin-Interfaces",
         "Lebensdauer und Mehrfachregistrierung kommen aus EFs Tabelle. Falsche "
         "Lebensdauern führen sonst zu Exceptions oder subtil falschem Verhalten."],
        [c("TryAddProviderSpecificServices"), "Eigene Dienste, die EF nicht kennt",
         "Der Builder lehnt unbekannte Typen ab; hier legt man die Lebensdauer selbst fest."],
        [c("services.Replace(...)"), "Einzeldienst des Providers ersetzen",
         "Der Provider registriert per " + c("TryAdd") + ". " + c("Replace") +
         " gewinnt unabhängig davon, ob das Plugin vor oder nach dem Provider läuft."],
    ], [1.3, 1.2, 2.2])
S += callout("hinweis", [
    "EF ruft " + c("ApplyServices") + " der Extensions in der Reihenfolge auf, in der sie "
    "hinzugefügt wurden. Läuft das Plugin zuerst, fügt " + c("Replace") + " den Dienst hinzu "
    "und das spätere " + c("TryAdd") + " des Providers greift nicht mehr. Läuft es danach, "
    "ersetzt " + c("Replace") + " den Provider-Dienst. Beide Reihenfolgen führen zum "
    "selben Ergebnis.",
], title="Warum Replace in beiden Reihenfolgen funktioniert")

S += h2("Singleton-Options")
S.append(p(
    "Singleton-Dienste wie Type-Mapping-Plugins können " + c("IDbContextOptions") +
    " nicht pro Context auflösen. Für sie gibt es " + c("ISingletonOptions") + ": ein "
    "Singleton, das EF einmal beim Erstellen des Providers mit den Options initialisiert "
    "und bei jedem weiteren Context validiert."))
S += code_block('''
public class TenancySingletonOptions : ITenancySingletonOptions
{
    public TenancySettings Settings { get; private set; } = TenancySettings.Default;

    // Einmal pro internem Service Provider
    public virtual void Initialize(IDbContextOptions options)
        => Settings = options.FindExtension<TenancyOptionsExtension>()?.Settings
                      ?? TenancySettings.Default;

    // Bei jedem Context: Optionen dürfen nicht vom Stand des Providers abweichen
    public virtual void Validate(IDbContextOptions options)
    {
        var settings = options.FindExtension<TenancyOptionsExtension>()?.Settings
                       ?? TenancySettings.Default;
        if (settings != Settings)
            throw new InvalidOperationException("Tenancy-Optionen nachträglich geändert ...");
    }
}
''', label="Infrastructure/ITenancySingletonOptions.cs")
S.append(p(
    "Die doppelte Registrierung in " + c("ApplyServices") + " ist nötig: einmal unter dem "
    "eigenen Interface, damit Dienste es injizieren können, und einmal als " +
    c("ISingletonOptions") + ", damit EF es findet und initialisiert. Die Factory-Variante "
    "sorgt dafür, dass beide Registrierungen <i>dieselbe</i> Instanz liefern."))

S += h2("Kombinierbarkeit trotz Ersetzen")
S.append(p(
    "Ersetzt ein Plugin einen Einzeldienst, kann ein zweites Plugin, das dasselbe tut, "
    "nicht gleichzeitig aktiv sein. Professionelle Plugins entschärfen das, indem sie die "
    "eigentliche Logik zusätzlich öffentlich und wiederverwendbar anbieten. Im Beispiel ist " +
    c("CreatePolicySql") + " öffentlich und alle Generator-Methoden sind virtuell. Ein "
    "Nutzer, der zwei Plugins kombinieren muss, schreibt einen eigenen Generator, der von "
    "einem erbt und die Logik des anderen aufruft. Diese Möglichkeit gehört in die "
    "Dokumentation."))
S.append(p(
    "Noch besser vermeidet ein Plugin den Konflikt ganz, indem es den Dienst "
    "<b>dekoriert</b> statt ersetzt: Es nimmt die Implementierung, die zu diesem Zeitpunkt "
    "registriert ist (die des Providers oder eines anderen Plugins), umhüllt sie und "
    "delegiert an sie. Ein Beispiel ist ein (fiktives) Audit-Plugin, das an jede neue "
    "Tabelle einen Trigger hängt. In seinem " + c("ApplyServices") + " sucht es die "
    "vorhandene Registrierung und ersetzt nur den Deskriptor, nicht die Logik:"))
S += code_block('''
public void ApplyServices(IServiceCollection services)
{
    // Bisher registriert: der Generator des Providers oder eines anderen Plugins
    var inner = services.LastOrDefault(d => d.ServiceType == typeof(IMigrationsSqlGenerator))
        ?? throw new InvalidOperationException("Call UseContosoAudit() after UseNpgsql().");

    services.Replace(ServiceDescriptor.Describe(
        typeof(IMigrationsSqlGenerator),
        sp => new AuditMigrationsSqlGenerator(
            (IMigrationsSqlGenerator)CreateInstance(sp, inner),
            sp.GetRequiredService<MigrationsSqlGeneratorDependencies>()),
        inner.Lifetime));     // Lebensdauer des Originals
}

// Erzeugt die Implementierung so, wie es der ursprüngliche Deskriptor getan hätte
private static object CreateInstance(IServiceProvider sp, ServiceDescriptor descriptor)
    => descriptor.ImplementationInstance
       ?? descriptor.ImplementationFactory?.Invoke(sp)
       ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
''', label="tests/Acme.EntityFrameworkCore.Tenancy.Tests/CompositionTests.cs › AuditOptionsExtension")
S.append(p(
    "Der Trick funktioniert, weil EF " + c("ApplyServices") + " in der Reihenfolge aufruft, "
    "in der die Extensions hinzugefügt wurden; wenn das Audit-Plugin läuft, hat der "
    "Provider seinen Generator bereits registriert. " + c("CreateInstance") + " bildet die "
    "ursprüngliche Registrierung exakt nach: eine Instanz, eine Factory oder einen Typ, "
    "dessen Abhängigkeiten (etwa Npgsqls Singleton-Options) " + c("ActivatorUtilities") +
    " aus dem Container auflöst. Der Decorator selbst nutzt nur die öffentliche "
    "Schnittstelle:"))
S += code_block('''
public class AuditMigrationsSqlGenerator : IMigrationsSqlGenerator
{
    // Konstruktor: (IMigrationsSqlGenerator inner, MigrationsSqlGeneratorDependencies deps)

    public IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
    {
        var commands = _inner.Generate(operations, model, options).ToList();   // unverändert
        var builder = new MigrationCommandListBuilder(_dependencies);
        var sql = _dependencies.SqlGenerationHelper;

        // Am Ende angehängt: dann existieren alle Tabellen der Migration
        foreach (var table in operations.OfType<CreateTableOperation>())
        {
            builder.Append("CREATE TRIGGER audit_changes ")
                .Append("AFTER INSERT OR UPDATE OR DELETE ON ")
                .Append(sql.DelimitIdentifier(table.Name, table.Schema))
                .Append(" FOR EACH ROW EXECUTE FUNCTION audit.log_change()")
                .AppendLine(sql.StatementTerminator)
                .EndCommand();
        }

        commands.AddRange(builder.GetCommandList());
        return commands;
    }
}
''', label="tests/.../CompositionTests.cs › AuditMigrationsSqlGenerator (gekürzt)")
S.append(p(
    "In der Anwendung lassen sich beide Plugins ohne eine Zeile Verbindungscode "
    "kombinieren:"))
S += code_block('''
services.AddDbContext<ShopContext>((sp, options) => options
    .UseNpgsql(connectionString)
    .UseAcmeTenancy(sp)       // ersetzt IMigrationsSqlGenerator
    .UseContosoAudit());      // umhüllt, was registriert ist: hier Acmes
''', label="Program.cs")
S += callout("wichtig", [
    "Decorators lassen sich in beliebiger Reihenfolge kombinieren, weil jeder umhüllt, was "
    "er vorfindet. Ein Plugin, das " + c("Replace") + " verwendet, verwirft dagegen alles, "
    "was vor ihm registriert wurde, auch Decorators. Ein ersetzendes Plugin muss deshalb "
    "zuerst aktiviert werden, und ein Decorator muss prüfen, ob es etwas zu dekorieren "
    "gibt, und sonst mit einer klaren Meldung abbrechen (Provider fehlt oder wurde später "
    "hinzugefügt).",
    "Dekorieren setzt voraus, dass die öffentliche Schnittstelle genügt. Das Tenancy-Plugin "
    "braucht geschützte Einstiegspunkte innerhalb von " + c("CREATE TABLE") + " und erbt "
    "deshalb vom Npgsql-Generator. Für solche Fälle bleibt der oben beschriebene Ausweg: "
    "öffentliche Logik wie " + c("CreatePolicySql") + ", virtuelle Methoden und " +
    c("ReplaceService") + " in der Anwendung.",
], title="Dekorieren, wo möglich; ersetzen, wo nötig")
S += callout("tipp", [
    "Der Test " + c("CompositionTests") + " im Beispiel enthält das vollständige "
    "Audit-Plugin. Er prüft, dass der Decorator Acmes Generator umhüllt, dass die Migration "
    "das SQL beider Plugins enthält und dass das Aktivieren vor dem Provider fehlschlägt.",
])

# ------------------------------------------------------------ 6
S += h1("Öffentliche API und Integration in die Anwendung")
S += h2("Die Use-Methode")
S.append(p(
    "Nutzer aktivieren das Plugin über eine Extension-Method auf " +
    c("DbContextOptionsBuilder") + ". Sie holt die vorhandene Extension oder legt eine neue "
    "an und registriert sie über " + c("IDbContextOptionsBuilderInfrastructure") + ":"))
S += code_block('''
public static DbContextOptionsBuilder UseAcmeTenancy(
    this DbContextOptionsBuilder optionsBuilder,
    Action<TenancyOptionsBuilder>? configure = null)
{
    ArgumentNullException.ThrowIfNull(optionsBuilder);

    var extension = optionsBuilder.Options.FindExtension<TenancyOptionsExtension>()
                    ?? new TenancyOptionsExtension();

    ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
    configure?.Invoke(new TenancyOptionsBuilder(optionsBuilder));
    return optionsBuilder;
}

// Generische Überladung: Method Chaining bleibt typisiert
public static DbContextOptionsBuilder<TContext> UseAcmeTenancy<TContext>(
    this DbContextOptionsBuilder<TContext> optionsBuilder,
    Action<TenancyOptionsBuilder>? configure = null) where TContext : DbContext
    => (DbContextOptionsBuilder<TContext>)UseAcmeTenancy(
        (DbContextOptionsBuilder)optionsBuilder, configure);
''', label="Infrastructure/TenancyDbContextOptionsBuilderExtensions.cs (Auszug)")
S.append(p(
    "Die Datei liegt im Namespace " + c("Microsoft.EntityFrameworkCore") + ". So ist die "
    "Methode ohne zusätzliches " + c("using") + " auffindbar, genau wie " +
    c("UseNpgsql") + ". Das ist die verbreitete Konvention im EF-Ökosystem."))

S += h2("Der Options-Builder")
S.append(p(
    "Einstellungen werden über einen eigenen Builder gesetzt. Er validiert Eingaben sofort "
    "und schreibt jede Änderung als neue Extension zurück:"))
S += code_block('''
public class TenancyOptionsBuilder
{
    public TenancyOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        OptionsBuilder = optionsBuilder;
    }

    protected virtual DbContextOptionsBuilder OptionsBuilder { get; }

    public virtual TenancyOptionsBuilder UseSettingName(string settingName)
        => With(s => s with
        {
            SettingName = TenancyIdentifiers.ValidateSettingName(settingName),
        });

    public virtual TenancyOptionsBuilder ApplyQueryFilter(bool apply = true)
        => With(s => s with { ApplyQueryFilter = apply });

    protected virtual TenancyOptionsBuilder With(
        Func<TenancySettings, TenancySettings> change)
    {
        var extension = OptionsBuilder.Options.FindExtension<TenancyOptionsExtension>()
                        ?? new TenancyOptionsExtension();

        ((IDbContextOptionsBuilderInfrastructure)OptionsBuilder)
            .AddOrUpdateExtension(extension.WithSettings(change(extension.Settings)));
        return this;
    }
}
''', label="Infrastructure/TenancyOptionsBuilder.cs (Auszug)")
S += callout("hinweis", [
    "Provider-spezifische Plugins werden manchmal auch über den Options-Builder des "
    "Providers aktiviert, etwa " + c("UseNpgsql(cs, o => o.UseNodaTime())") + ". Dafür "
    "schreibt man eine Extension-Method auf " + c("NpgsqlDbContextOptionsBuilder") + " und "
    "kommt über " + c("IRelationalDbContextOptionsBuilderInfrastructure.OptionsBuilder") +
    " an den eigentlichen Builder. Das Beispiel nutzt den einfacheren Weg über " +
    c("DbContextOptionsBuilder") + ", weil das Plugin mehr ist als eine Provider-Option.",
], title="Alternative: Aktivierung über den Provider-Builder")

S += h2("Zwei Container verbinden")
S.append(p(
    "Manche Teile eines Plugins brauchen Dienste der <i>Anwendung</i>: Der "
    "Connection-Interceptor braucht den aktuellen Mandanten, den nur die Anwendung kennt. "
    "Der interne EF-Provider kann App-Dienste aber nicht auflösen. Die saubere Lösung: "
    "Solche Komponenten leben im App-Container und werden per " + c("AddInterceptors") +
    " an EF übergeben."))
S += code_block('''
// Kernpaket: nur den Mandanten-Provider im App-Container registrieren
public static IServiceCollection AddAcmeTenancy<TTenantProvider>(
    this IServiceCollection services,
    ServiceLifetime lifetime = ServiceLifetime.Scoped)
    where TTenantProvider : class, ITenantProvider
{
    services.TryAdd(ServiceDescriptor.Describe(
        typeof(ITenantProvider), typeof(TTenantProvider), lifetime));
    return services;
}

// Provider-Paket: Use-Überladung erzeugt die Interceptors aus diesem Provider
public static DbContextOptionsBuilder UseAcmeTenancy(
    this DbContextOptionsBuilder optionsBuilder,
    IServiceProvider applicationServices,
    Action<TenancyOptionsBuilder>? configure = null)
{
    var tenants = applicationServices.GetRequiredService<ITenantProvider>();

    return optionsBuilder
        .UseAcmeTenancy(configure)
        .AddInterceptors(
            new TenantConnectionInterceptor(tenants),
            new TenantSaveChangesInterceptor(tenants));
}
''', label="DependencyInjection/… (Kern), Infrastructure/… (PostgreSQL), Auszüge")
S.append(p(
    "Bei " + c("AddDbContext((sp, o) => ...)") + " ist " + c("sp") + " der Scope der "
    "aktuellen Anfrage, weil EF die Options standardmäßig pro Scope baut. Der "
    "Mandanten-Provider und damit die Interceptors sehen den richtigen Mandanten. "
    "Die Registrierung liegt im Kernpaket, weil sie für jeden Provider gleich ist "
    "(Kapitel 41). Für die Brücke in die andere "
    "Richtung kann ein Interceptor über " + c("eventData.Context.GetService<T>()") +
    " Dienste aus dem <i>internen</i> Provider lesen; der Connection-Interceptor holt sich "
    "so den Setting-Namen aus den Singleton-Options (Kapitel 15)."))
S += callout("wichtig", [
    "Bei " + c("AddDbContextPool") + " werden Contexts wiederverwendet und bekommen ihre "
    "Options nur einmal. Scoped-Interceptors aus dem ersten Scope würden für alle späteren "
    "Anfragen weiterverwendet, mit dem Mandanten der ersten Anfrage. Für Pooling muss der "
    "Interceptor ein Singleton sein, der den Mandanten bei jedem Aufruf neu ermittelt, etwa "
    "über " + c("IHttpContextAccessor") + ". Kapitel 35 zeigt diese Variante. Dasselbe gilt "
    "für " + c("AddDbContextFactory") + ", dessen Factory standardmäßig ein Singleton ist.",
], title="DbContext-Pooling")
