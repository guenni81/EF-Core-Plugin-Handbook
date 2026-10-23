namespace Acme.EntityFrameworkCore.Tenancy;

/// <summary>Violation of tenant separation that the plugin detects before the database does.</summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> like EF's own errors, so existing
/// handlers keep working.
/// </remarks>
public class TenancyException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The error message.</param>
    public TenancyException(string message)
        : base(message)
    {
    }
}
