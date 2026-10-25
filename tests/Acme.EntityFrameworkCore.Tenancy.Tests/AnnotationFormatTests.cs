using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// The serialized form of an annotation is stored in every user's model snapshot. If it
/// changes between plugin versions, the differ sees a different string and every user gets a
/// phantom migration after the upgrade. These tests pin the format (chapter "Versioning and
/// compatibility").
/// </summary>
public class AnnotationFormatTests
{
    [Fact]
    public void Serialized_policy_format_is_stable()
    {
        var policy = new RlsPolicy("support_read", PolicyCommand.Select, Permissive: false,
            Roles: ["shop_support"], Using: "true", WithCheck: null);

        // Golden value: changing it is a breaking change for existing snapshots
        Assert.Equal(
            """{"Name":"support_read","Command":"Select","Permissive":false"""
            + ""","Roles":["shop_support"],"Using":"true","WithCheck":null}""",
            policy.Serialize());
    }

    [Fact]
    public void Missing_properties_are_read_as_defaults()
    {
        // An older version without "Roles" and "WithCheck"
        var policy = RlsPolicy.Deserialize(
            """{"Name":"t","Command":"All","Permissive":true,"Using":"a"}""");

        Assert.Null(policy.Roles);
        Assert.Null(policy.WithCheck);
    }

    [Fact]
    public void Unknown_properties_are_ignored()
    {
        // A newer version with an additional property: older plugin versions can still read it
        var policy = RlsPolicy.Deserialize(
            """{"Name":"t","Command":"All","Permissive":true,"Using":"a","Comment":"new"}""");

        Assert.Equal("a", policy.Using);
    }
}
