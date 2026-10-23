using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Acme.EntityFrameworkCore.Tenancy.ValueGeneration;

/// <summary>
/// Creates <see cref="TenantIdValueGenerator"/> instances. Configured via
/// <c>HasValueGeneratorFactory(Type)</c>: the compiled model generator of EF Core 10 only
/// accepts a factory <b>type</b> with a public parameterless constructor and rejects both
/// factory delegates and <c>HasValueGenerator(Type)</c>.
/// </summary>
public class TenantIdValueGeneratorFactory : ValueGeneratorFactory
{
    /// <summary>
    /// Returns a new generator. The generator is stateless, so EF may cache and share it
    /// between contexts without any synchronization.
    /// </summary>
    public override ValueGenerator Create(IProperty property, ITypeBase typeBase)
        => new TenantIdValueGenerator();
}
