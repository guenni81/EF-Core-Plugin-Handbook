namespace Acme.EntityFrameworkCore.Tenancy;

/// <summary>
/// Marks classes containing the plugin's EF.Functions methods. The evaluatable expression
/// filter prevents client-side evaluation for all methods marked this way, including methods
/// from provider packages.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TenancyDbFunctionsAttribute : Attribute;
