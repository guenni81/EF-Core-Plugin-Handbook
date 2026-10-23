using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Design.Internal;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Design.Internal;

/// <summary>
/// Controls which annotations end up in the compiled model.
/// </summary>
/// <remarks>
/// EF copies simple annotations (strings, numbers, bool) into the compiled model automatically.
/// A custom generator is therefore needed to (1) filter out pure design-time annotations
/// that nobody reads at runtime (here: RLS settings and policies), and (2) translate annotations
/// with complex values into C# code. The runtime annotation
/// "Acme:Tenancy:TenantProperty" is deliberately kept.
/// <para>
/// Built on internal APIs (EF1001): the base class lives in
/// Npgsql.EntityFrameworkCore.PostgreSQL.Design.Internal, the dependencies and parameters
/// in Microsoft.EntityFrameworkCore.Design.Internal.
/// </para>
/// </remarks>
public class TenancyCSharpRuntimeAnnotationCodeGenerator : NpgsqlCSharpRuntimeAnnotationCodeGenerator
{
    /// <summary>Creates the generator.</summary>
    /// <param name="dependencies">Core dependencies of the runtime annotation code generator.</param>
    /// <param name="relationalDependencies">Relational dependencies of the generator.</param>
    public TenancyCSharpRuntimeAnnotationCodeGenerator(
        CSharpRuntimeAnnotationCodeGeneratorDependencies dependencies,
        RelationalCSharpRuntimeAnnotationCodeGeneratorDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    /// <summary>Removes the RLS annotations of an entity type before the base class runs.</summary>
    /// <param name="entityType">The entity type being compiled.</param>
    /// <param name="parameters">Generation parameters, including the annotations to write.</param>
    public override void Generate(IEntityType entityType, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        RemoveDesignTimeAnnotations(parameters);
        base.Generate(entityType, parameters);
    }

    /// <summary>
    /// Removes the RLS annotations of a relational table before the base class writes the rest.
    /// Only relevant when the relational model is compiled as well (NativeAOT).
    /// </summary>
    /// <param name="table">The relational table being compiled.</param>
    /// <param name="parameters">Generation parameters, including the annotations to write.</param>
    public override void Generate(ITable table, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        // TenancyAnnotationProvider copies the RLS annotations onto the relational table,
        // so they show up a second time here when the relational model is generated
        // (ForNativeAot) and must be filtered as well.
        RemoveDesignTimeAnnotations(parameters);
        base.Generate(table, parameters);
    }

    private static void RemoveDesignTimeAnnotations(CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        // The generator runs once for regular and once for runtime annotations (IsRuntime);
        // the RLS keys are regular annotations, so only that pass needs filtering
        if (parameters.IsRuntime)
            return;

        foreach (var key in parameters.Annotations.Keys
                     .Where(k => k.StartsWith(RlsAnnotationNames.Prefix, StringComparison.Ordinal))
                     .ToList())
        {
            parameters.Annotations.Remove(key);
        }
    }
}
