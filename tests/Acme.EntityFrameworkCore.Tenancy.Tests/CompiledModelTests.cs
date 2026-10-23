using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// Generates a compiled model like "dotnet ef dbcontext optimize". The generator fails if a
/// type mapping has no public static <c>Default</c> property or a value generator is only
/// configured as a factory delegate instead of a type.
/// </summary>
public class CompiledModelTests
{
    [Fact]
    public void Compiled_model_can_be_generated()
    {
        using var ctx = Contexts.Create();
        using var services = DesignServices.Create(ctx);
        var outputDir = Directory.CreateTempSubdirectory("acme-compiled-").FullName;

        try
        {
            var files = services.GetRequiredService<ICompiledModelScaffolder>().ScaffoldModel(
                ctx.GetService<IDesignTimeModel>().Model,
                outputDir,
                new CompiledModelCodeGenerationOptions
                {
                    ContextType = typeof(TestContext),
                    ModelNamespace = "Generated",
                    Language = "C#",
                    UseNullableReferenceTypes = true,
                    ForNativeAot = true,
                });

            var code = string.Concat(files.Select(File.ReadAllText));

            Assert.Contains("TenantIdTypeMapping.Default", code);
            Assert.Contains("TenantIdValueGeneratorFactory", code);

            // The runtime annotation is kept; design-time annotations (RLS policies) are filtered
            // out by the plugin's ICSharpRuntimeAnnotationCodeGenerator, nobody reads them at run time.
            Assert.Contains("Acme:Tenancy:TenantProperty", code);
            Assert.DoesNotContain("Acme:Rls:Policy:", code);
        }
        finally
        {
            Directory.Delete(outputDir, recursive: true);
        }
    }
}
