// -----------------------------------------------------------------------
// <copyright file="MetadataSerializerTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using Compendium.Adapters.Pinecone.Internal;

namespace Compendium.Adapters.Pinecone.Tests.Internal;

public class MetadataSerializerTests
{
    [Fact]
    public void ToMetadata_NullMetadataAndNoTenant_ReturnsNull()
    {
        // Arrange / Act
        var actual = MetadataSerializer.ToMetadata(null, null);

        // Assert
        actual.Should().BeNull();
    }

    [Fact]
    public void ToMetadata_EmptyMetadataAndNoTenant_ReturnsNull()
    {
        // Arrange / Act
        var actual = MetadataSerializer.ToMetadata(new Dictionary<string, object>(), null);

        // Assert
        actual.Should().BeNull();
    }

    [Fact]
    public void ToMetadata_NullMetadataWithTenant_ReturnsDictWithTenantKey()
    {
        // Arrange / Act
        var actual = MetadataSerializer.ToMetadata(null, "tenant-1");

        // Assert
        actual.Should().NotBeNull();
        actual![MetadataSerializer.TenantMetadataKey].Should().Be("tenant-1");
        actual.Should().HaveCount(1);
    }

    [Fact]
    public void ToMetadata_CopiesAllEntriesAndInjectsTenant()
    {
        // Arrange
        var input = new Dictionary<string, object> { ["title"] = "hello", ["score"] = 42 };

        // Act
        var actual = MetadataSerializer.ToMetadata(input, "tenant-1");

        // Assert
        actual.Should().NotBeNull();
        actual!["title"].Should().Be("hello");
        actual["score"].Should().Be(42);
        actual[MetadataSerializer.TenantMetadataKey].Should().Be("tenant-1");
    }

    [Fact]
    public void ToMetadata_NoTenant_DoesNotInjectKey()
    {
        // Arrange
        var input = new Dictionary<string, object> { ["title"] = "hello" };

        // Act
        var actual = MetadataSerializer.ToMetadata(input, null);

        // Assert
        actual.Should().NotBeNull();
        actual.Should().NotContainKey(MetadataSerializer.TenantMetadataKey);
    }

    [Fact]
    public void FromMetadata_Null_ReturnsEmpty()
    {
        // Arrange / Act
        var actual = MetadataSerializer.FromMetadata(null);

        // Assert
        actual.Should().BeEmpty();
    }

    [Fact]
    public void FromMetadata_EmptyDict_ReturnsEmpty()
    {
        // Arrange / Act
        var actual = MetadataSerializer.FromMetadata(new Dictionary<string, object?>());

        // Assert
        actual.Should().BeEmpty();
    }

    [Fact]
    public void FromMetadata_StripsTenantKey()
    {
        // Arrange
        var input = new Dictionary<string, object?>
        {
            ["title"] = "alpha",
            [MetadataSerializer.TenantMetadataKey] = "tenant-1",
        };

        // Act
        var actual = MetadataSerializer.FromMetadata(input);

        // Assert
        actual.Should().ContainKey("title");
        actual.Should().NotContainKey(MetadataSerializer.TenantMetadataKey);
    }

    [Fact]
    public void FromMetadata_SkipsNullValues()
    {
        // Arrange
        var input = new Dictionary<string, object?> { ["a"] = null, ["b"] = "x" };

        // Act
        var actual = MetadataSerializer.FromMetadata(input);

        // Assert
        actual.Should().NotContainKey("a");
        actual.Should().ContainKey("b");
    }

    [Fact]
    public void FromMetadata_UnwrapsJsonElementTypes()
    {
        // Arrange — Pinecone deserialises into JsonElement for `object` properties.
        // Note: a JSON-null value wraps in a non-null JsonElement (ValueKind=Null) and
        // is unwrapped to "" rather than dropped; only literal C# null is skipped.
        using var doc = JsonDocument.Parse("""
        {
          "s": "hello",
          "i": 42,
          "d": 3.14,
          "b": true,
          "n": null,
          "arr": [1, 2, 3],
          "obj": { "k": "v" }
        }
        """);

        var input = doc.RootElement
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value);

        // Act
        var actual = MetadataSerializer.FromMetadata(input);

        // Assert
        actual["s"].Should().Be("hello");
        actual["i"].Should().Be(42L);
        actual["d"].Should().BeOfType<double>().And.Be(3.14);
        actual["b"].Should().Be(true);
        actual["n"].Should().Be(string.Empty); // JSON null is unwrapped to "" by Unwrap
        actual["arr"].Should().BeOfType<object[]>();
        actual["obj"].Should().BeAssignableTo<IDictionary<string, object>>();
    }

    [Fact]
    public void ExtractTenantId_FromPlainString_ReturnsString()
    {
        // Arrange
        var input = new Dictionary<string, object?>
        {
            [MetadataSerializer.TenantMetadataKey] = "tenant-1",
        };

        // Act
        var actual = MetadataSerializer.ExtractTenantId(input);

        // Assert
        actual.Should().Be("tenant-1");
    }

    [Fact]
    public void ExtractTenantId_FromJsonElement_ReturnsString()
    {
        // Arrange
        using var doc = JsonDocument.Parse("""{ "tenant_id": "tenant-2" }""");
        var input = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);

        // Act
        var actual = MetadataSerializer.ExtractTenantId(input);

        // Assert
        actual.Should().Be("tenant-2");
    }

    [Fact]
    public void ExtractTenantId_Missing_ReturnsNull()
    {
        // Arrange / Act
        var actual = MetadataSerializer.ExtractTenantId(new Dictionary<string, object?>());

        // Assert
        actual.Should().BeNull();
    }

    [Fact]
    public void ExtractTenantId_NullDict_ReturnsNull()
    {
        // Arrange / Act
        var actual = MetadataSerializer.ExtractTenantId(null);

        // Assert
        actual.Should().BeNull();
    }

    [Fact]
    public void ExtractTenantId_NullValue_ReturnsNull()
    {
        // Arrange
        var input = new Dictionary<string, object?> { [MetadataSerializer.TenantMetadataKey] = null };

        // Act
        var actual = MetadataSerializer.ExtractTenantId(input);

        // Assert
        actual.Should().BeNull();
    }

    [Fact]
    public void ExtractTenantId_NonStringValue_FallsBackToToString()
    {
        // Arrange — non-string, non-JsonElement triggers the fallback ToString() branch.
        var input = new Dictionary<string, object?>
        {
            [MetadataSerializer.TenantMetadataKey] = 42,
        };

        // Act
        var actual = MetadataSerializer.ExtractTenantId(input);

        // Assert
        actual.Should().Be("42");
    }

    [Fact]
    public void FromMetadata_UnwrapsJsonFalse()
    {
        // Arrange — exercise the JsonValueKind.False branch.
        using var doc = JsonDocument.Parse("""{ "off": false }""");
        var input = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);

        // Act
        var actual = MetadataSerializer.FromMetadata(input);

        // Assert
        actual["off"].Should().Be(false);
    }
}
