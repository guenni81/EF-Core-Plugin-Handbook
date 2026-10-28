using Acme.EntityFrameworkCore.Functions.PostgreSQL.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Activation through the provider's options builder:
/// <c>options.UseNpgsql(cs, o =&gt; o.UseAcmeFunctions())</c>.
/// </summary>
public static class FunctionsNpgsqlDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Registers <c>EF.Functions.IsNotDistinctFrom</c>, <c>IsDistinctFrom</c> and <c>Every</c>.
    /// </summary>
    /// <param name="npgsqlOptionsBuilder">The builder passed to <c>UseNpgsql</c>.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static NpgsqlDbContextOptionsBuilder UseAcmeFunctions(
        this NpgsqlDbContextOptionsBuilder npgsqlOptionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(npgsqlOptionsBuilder);

        var optionsBuilder = ((IRelationalDbContextOptionsBuilderInfrastructure)npgsqlOptionsBuilder)
            .OptionsBuilder;

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
            optionsBuilder.Options.FindExtension<FunctionsOptionsExtension>()
            ?? new FunctionsOptionsExtension());

        return npgsqlOptionsBuilder;
    }
}
