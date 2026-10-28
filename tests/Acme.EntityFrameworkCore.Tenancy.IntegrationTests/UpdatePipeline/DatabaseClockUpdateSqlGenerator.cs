using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Update;
using Npgsql.EntityFrameworkCore.PostgreSQL.Update.Internal;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests.UpdatePipeline;

// TEACHING EXAMPLE for chapter "The update pipeline", not part of the tenancy packages: it shows
// how a plugin changes the shape of the SQL that SaveChanges writes.

/// <summary>Fluent API of the example: the column takes the database clock on every UPDATE.</summary>
public static class DatabaseClockPropertyBuilderExtensions
{
    /// <summary>Annotation that marks the property; read by the update SQL generator.</summary>
    public const string Annotation = "Acme:Example:DatabaseClockOnUpdate";

    /// <summary>
    /// INSERT takes now() as the column default, UPDATE sets the column to now(); EF reads the
    /// value back with RETURNING and never sends one of its own.
    /// </summary>
    /// <typeparam name="T">The property type, a timestamp.</typeparam>
    /// <param name="builder">The property builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static PropertyBuilder<T> HasDatabaseClockOnUpdate<T>(this PropertyBuilder<T> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Metadata.SetAnnotation(Annotation, true);
        return builder.HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();
    }
}

/// <summary>
/// Npgsql's update SQL generator with one change: UPDATE also assigns now() to every column
/// marked with <see cref="DatabaseClockPropertyBuilderExtensions.HasDatabaseClockOnUpdate{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Derives from <c>NpgsqlUpdateSqlGenerator</c>, which lives in an *.Internal namespace (internal
/// Npgsql API, EF1001): there is exactly one IUpdateSqlGenerator per context, and replacing it
/// with EF's base class would lose Npgsql's own SQL (JSON partial updates, RETURNING handling).
/// </para>
/// <para>
/// A generated-on-update column is a read operation for EF: it is in RETURNING but not in SET.
/// The override adds the assignment; everything else, including the WHERE clause with the
/// concurrency token and the RETURNING clause, is EF's.
/// </para>
/// </remarks>
public class DatabaseClockUpdateSqlGenerator : NpgsqlUpdateSqlGenerator
{
    /// <summary>Creates the generator.</summary>
    /// <param name="dependencies">The update SQL generator dependencies.</param>
    public DatabaseClockUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>UPDATE ... SET written columns, plus "column" = now() for marked read columns.</summary>
    /// <param name="commandStringBuilder">The builder the SQL is appended to.</param>
    /// <param name="name">The table name.</param>
    /// <param name="schema">The table schema.</param>
    /// <param name="writeOperations">Columns with a value from the application.</param>
    /// <param name="readOperations">Columns read back after the update.</param>
    /// <param name="conditionOperations">Key and concurrency token for the WHERE clause.</param>
    /// <param name="appendReturningOneClause">Whether to return 1 when nothing is read back.</param>
    protected override void AppendUpdateCommand(
        StringBuilder commandStringBuilder,
        string name,
        string? schema,
        IReadOnlyList<IColumnModification> writeOperations,
        IReadOnlyList<IColumnModification> readOperations,
        IReadOnlyList<IColumnModification> conditionOperations,
        bool appendReturningOneClause = false)
    {
        ArgumentNullException.ThrowIfNull(commandStringBuilder);
        ArgumentNullException.ThrowIfNull(readOperations);

        AppendUpdateCommandHeader(commandStringBuilder, name, schema, writeOperations);

        // The same order as EF's AppendUpdateCommand, with the extra assignments after SET
        // (the header always writes SET; EF sends no UPDATE without a written column)
        foreach (var column in readOperations.Where(IsDatabaseClock))
        {
            commandStringBuilder.Append(", ");
            SqlGenerationHelper.DelimitIdentifier(commandStringBuilder, column.ColumnName);
            commandStringBuilder.Append(" = now()");
        }

        AppendWhereClause(commandStringBuilder, conditionOperations);
        AppendReturningClause(commandStringBuilder, readOperations, appendReturningOneClause ? "1" : null);
        commandStringBuilder.AppendLine(SqlGenerationHelper.StatementTerminator);
    }

    private static bool IsDatabaseClock(IColumnModification column)
        => column.Property?[DatabaseClockPropertyBuilderExtensions.Annotation] is true;
}
