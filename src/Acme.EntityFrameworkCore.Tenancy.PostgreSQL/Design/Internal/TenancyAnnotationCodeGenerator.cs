using System.Reflection;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Builders;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.EntityFrameworkCore.PostgreSQL.Design.Internal;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Design.Internal;

/// <summary>
/// Writes RLS annotations in the model snapshot as readable Fluent API calls
/// (HasRowLevelSecurity / HasPolicy) instead of HasAnnotation(...) with JSON.
/// </summary>
/// <remarks>
/// Inherits from the Npgsql generator: there is only one IAnnotationCodeGenerator.
/// <c>NpgsqlAnnotationCodeGenerator</c> lives in an *.Internal namespace (internal Npgsql
/// API, EF1001) and may change without notice.
/// </remarks>
public class TenancyAnnotationCodeGenerator : NpgsqlAnnotationCodeGenerator
{
    /// <summary>Creates the generator.</summary>
    /// <param name="dependencies">Dependencies of the annotation code generator.</param>
    public TenancyAnnotationCodeGenerator(AnnotationCodeGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    // The snapshot works with the NON-generic EntityTypeBuilder
    private static readonly MethodInfo HasRowLevelSecurityMethod =
        typeof(RlsEntityTypeBuilderExtensions).GetRuntimeMethod(
            nameof(RlsEntityTypeBuilderExtensions.HasRowLevelSecurity),
            [typeof(EntityTypeBuilder), typeof(bool)])!;

    private static readonly MethodInfo HasPolicyMethod =
        typeof(RlsEntityTypeBuilderExtensions).GetRuntimeMethod(
            nameof(RlsEntityTypeBuilderExtensions.HasPolicy),
            [typeof(EntityTypeBuilder), typeof(string), typeof(Action<PolicyBuilder>)])!;

    private static MethodInfo PolicyMethod(string name) => typeof(PolicyBuilder).GetMethod(name)!;

    /// <summary>
    /// Translates RLS annotations of an entity type into HasRowLevelSecurity/HasPolicy calls,
    /// then lets the Npgsql generator handle the remaining annotations.
    /// </summary>
    /// <param name="entityType">The entity type being written.</param>
    /// <param name="annotations">Annotations still to be written; translated ones are removed.</param>
    /// <returns>The generated Fluent API calls.</returns>
    public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
        IEntityType entityType, IDictionary<string, IAnnotation> annotations)
    {
        var calls = new List<MethodCallCodeFragment>();

        // CONTRACT: REMOVE translated annotations from the dictionary.
        // Whatever remains is still written by EF as HasAnnotation(...).

        // Translate "true" only; an opt-out (false) is kept as HasAnnotation
        if (annotations.TryGetValue(RlsAnnotationNames.Enabled, out var enabled)
            && enabled.Value is true)
        {
            annotations.Remove(RlsAnnotationNames.Enabled);
            var forced = annotations.Remove(RlsAnnotationNames.Forced, out var forcedAnnotation)
                         && forcedAnnotation.Value is true;

            calls.Add(new MethodCallCodeFragment(HasRowLevelSecurityMethod, forced));
        }

        // Copy of the keys: the dictionary is modified inside the loop
        var policyKeys = annotations.Keys
            .Where(k => k.StartsWith(RlsAnnotationNames.PolicyPrefix, StringComparison.Ordinal))
            .ToList();

        foreach (var key in policyKeys)
        {
            var policy = RlsPolicy.Deserialize((string)annotations[key].Value!);
            annotations.Remove(key);

            calls.Add(new MethodCallCodeFragment(
                HasPolicyMethod,
                policy.Name,
                new NestedClosureCodeFragment("p", BuildPolicyChain(policy))));
        }

        calls.AddRange(base.GenerateFluentApiCalls(entityType, annotations));
        return calls;
    }

    /// <summary>Reconstructs p.For(...).Restrictive().To(...).Using(...).WithCheck(...).</summary>
    private static MethodCallCodeFragment BuildPolicyChain(RlsPolicy policy)
    {
        var calls = new List<MethodCallCodeFragment>();

        // Omit default values: as concise as hand-written code
        if (policy.Command != PolicyCommand.All)
            calls.Add(new(PolicyMethod(nameof(PolicyBuilder.For)), policy.Command));

        if (!policy.Permissive)
            calls.Add(new(PolicyMethod(nameof(PolicyBuilder.Restrictive))));

        if (policy.Roles is { Length: > 0 })
        {
            // Wrap explicitly in object[]! Otherwise, due to array covariance, the string[]
            // itself would be interpreted as the params argument list.
            calls.Add(new(PolicyMethod(nameof(PolicyBuilder.To)), new object?[] { policy.Roles }));
        }

        if (policy.Using is not null)
            calls.Add(new(PolicyMethod(nameof(PolicyBuilder.Using)), policy.Using));

        if (policy.WithCheck is not null)
            calls.Add(new(PolicyMethod(nameof(PolicyBuilder.WithCheck)), policy.WithCheck));

        // At least Using or WithCheck always exists (validated in PolicyBuilder)
        return calls.Skip(1).Aggregate(calls[0], (chain, next) => chain.Chain(next));
    }
}
