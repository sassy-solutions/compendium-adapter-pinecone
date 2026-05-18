// -----------------------------------------------------------------------
// <copyright file="VectorFilterTranslatorTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Pinecone.Internal;
using Compendium.Adapters.Pinecone.Options;

namespace Compendium.Adapters.Pinecone.Tests.Internal;

public class VectorFilterTranslatorTests
{
    [Fact]
    public void Build_NullFilterNoTenant_ReturnsEmptyTranslation()
    {
        // Arrange / Act
        var actual = VectorFilterTranslator.Build(null, null, PineconeTenancyMode.Namespace);

        // Assert
        actual.IsSuccess.Should().BeTrue();
        actual.Value.MetadataFilter.Should().BeNull();
        actual.Value.Namespace.Should().BeNull();
    }

    [Fact]
    public void Build_NamespaceMode_TenantId_MovesToNamespace()
    {
        // Arrange
        var filter = VectorFilter.Eq("category", "support").ForTenant("tenant-1");

        // Act
        var actual = VectorFilterTranslator.Build(filter, null, PineconeTenancyMode.Namespace);

        // Assert
        actual.IsSuccess.Should().BeTrue();
        actual.Value.Namespace.Should().Be("tenant-1");
        actual.Value.MetadataFilter.Should().NotBeNull();
        actual.Value.MetadataFilter!.Should().ContainKey("category");
        actual.Value.MetadataFilter.Should().NotContainKey(MetadataSerializer.TenantMetadataKey);
    }

    [Fact]
    public void Build_MetadataMode_TenantId_MovesToTopLevelAndClause()
    {
        // Arrange
        var filter = VectorFilter.Eq("category", "support").ForTenant("tenant-1");

        // Act
        var actual = VectorFilterTranslator.Build(filter, null, PineconeTenancyMode.Metadata);

        // Assert
        actual.IsSuccess.Should().BeTrue();
        actual.Value.Namespace.Should().BeNull();
        var meta = actual.Value.MetadataFilter ?? throw new InvalidOperationException();
        meta.Should().ContainKey("$and");
        meta["$and"].Should().BeAssignableTo<IEnumerable<object?>>();
    }

    [Fact]
    public void Build_MetadataMode_TenantOnlyNoFilter_ReturnsTenantEqClause()
    {
        // Arrange / Act
        var actual = VectorFilterTranslator.Build(null, "tenant-2", PineconeTenancyMode.Metadata);

        // Assert
        actual.IsSuccess.Should().BeTrue();
        actual.Value.MetadataFilter.Should().NotBeNull();
        actual.Value.MetadataFilter!.Should().ContainKey(MetadataSerializer.TenantMetadataKey);
    }

    [Fact]
    public void Build_InvalidTenantId_ReturnsValidation()
    {
        // Arrange / Act
        var actual = VectorFilterTranslator.Build(null, "bad tenant", PineconeTenancyMode.Namespace);

        // Assert
        actual.IsSuccess.Should().BeFalse();
        actual.Error.Code.Should().Be("Pinecone.InvalidTenantId");
    }

    [Fact]
    public void Build_TenantOverridePrefersExplicit()
    {
        // Arrange
        var filter = VectorFilter.Eq("category", "x").ForTenant("from-filter");

        // Act
        var actual = VectorFilterTranslator.Build(filter, "from-override", PineconeTenancyMode.Namespace);

        // Assert
        actual.Value.Namespace.Should().Be("from-override");
    }

    [Fact]
    public void Build_Eq_EmitsDollarEq()
    {
        // Arrange
        var filter = VectorFilter.Eq("category", "support");

        // Act
        var actual = VectorFilterTranslator.Build(filter, null, PineconeTenancyMode.Namespace);

        // Assert
        actual.IsSuccess.Should().BeTrue();
        var inner = (IDictionary<string, object?>)actual.Value.MetadataFilter!["category"]!;
        inner["$eq"].Should().Be("support");
    }

    [Fact]
    public void Build_Ne_EmitsDollarNe()
    {
        // Arrange
        var filter = VectorFilter.Ne("category", "blocked");

        // Act
        var actual = VectorFilterTranslator.Build(filter, null, PineconeTenancyMode.Namespace);

        // Assert
        var inner = (IDictionary<string, object?>)actual.Value.MetadataFilter!["category"]!;
        inner["$ne"].Should().Be("blocked");
    }

    [Fact]
    public void Build_In_EmitsDollarIn()
    {
        // Arrange
        var filter = VectorFilter.In("category", new object[] { "a", "b", "c" });

        // Act
        var actual = VectorFilterTranslator.Build(filter, null, PineconeTenancyMode.Namespace);

        // Assert
        var inner = (IDictionary<string, object?>)actual.Value.MetadataFilter!["category"]!;
        var values = (IEnumerable<object?>)inner["$in"]!;
        values.Should().HaveCount(3);
    }

    [Fact]
    public void Build_In_EmptyValues_RejectedByFactory()
    {
        // Arrange / Act — the VectorFilter factory itself validates non-empty values,
        // so this is enforced one layer up. Confirm the contract here.
        var act = () => VectorFilter.In("category", Array.Empty<object>());

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_Range_BothBounds_InclusiveExclusive()
    {
        // Arrange — min exclusive, max inclusive.
        var filter = VectorFilter.Range("score", min: 0.5, max: 1.0, minInclusive: false, maxInclusive: true);

        // Act
        var actual = VectorFilterTranslator.Build(filter, null, PineconeTenancyMode.Namespace);

        // Assert
        var inner = (IDictionary<string, object?>)actual.Value.MetadataFilter!["score"]!;
        inner.Should().ContainKey("$gt");
        inner.Should().ContainKey("$lte");
        inner.Should().NotContainKey("$gte");
        inner.Should().NotContainKey("$lt");
    }

    [Fact]
    public void Build_Range_MinOnly_Inclusive_EmitsGte()
    {
        // Arrange
        var filter = VectorFilter.Range("score", min: 0.5, max: null, minInclusive: true, maxInclusive: true);

        // Act
        var actual = VectorFilterTranslator.Build(filter, null, PineconeTenancyMode.Namespace);

        // Assert
        var inner = (IDictionary<string, object?>)actual.Value.MetadataFilter!["score"]!;
        inner.Should().ContainKey("$gte");
        inner.Should().NotContainKey("$lt");
        inner.Should().NotContainKey("$lte");
    }

    [Fact]
    public void Build_Range_NoBounds_RejectedByFactory()
    {
        // Arrange / Act — VectorFilter.Range itself validates at least one bound.
        var act = () => VectorFilter.Range("score", min: null, max: null);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_And_EmitsDollarAnd()
    {
        // Arrange
        var filter = VectorFilter.And(VectorFilter.Eq("a", 1), VectorFilter.Eq("b", 2));

        // Act
        var actual = VectorFilterTranslator.Build(filter, null, PineconeTenancyMode.Namespace);

        // Assert
        actual.IsSuccess.Should().BeTrue();
        actual.Value.MetadataFilter!.Should().ContainKey("$and");
    }

    [Fact]
    public void Build_Or_EmitsDollarOr()
    {
        // Arrange
        var filter = VectorFilter.Or(VectorFilter.Eq("a", 1), VectorFilter.Eq("b", 2));

        // Act
        var actual = VectorFilterTranslator.Build(filter, null, PineconeTenancyMode.Namespace);

        // Assert
        actual.IsSuccess.Should().BeTrue();
        actual.Value.MetadataFilter!.Should().ContainKey("$or");
    }

    [Fact]
    public void Build_AndChildFailure_Propagates()
    {
        // Arrange — child uses the reserved tenant_id field which the translator rejects.
        var filter = VectorFilter.And(
            VectorFilter.Eq("a", 1),
            VectorFilter.Eq(MetadataSerializer.TenantMetadataKey, "x"));

        // Act
        var actual = VectorFilterTranslator.Build(filter, null, PineconeTenancyMode.Namespace);

        // Assert
        actual.IsSuccess.Should().BeFalse();
        actual.Error.Code.Should().Be("Pinecone.ReservedFilterField");
    }

    [Fact]
    public void Build_OrChildFailure_Propagates()
    {
        // Arrange — same reserved-field guard, this time inside an Or branch.
        var filter = VectorFilter.Or(
            VectorFilter.Eq("a", 1),
            VectorFilter.Eq(MetadataSerializer.TenantMetadataKey, "x"));

        // Act
        var actual = VectorFilterTranslator.Build(filter, null, PineconeTenancyMode.Namespace);

        // Assert
        actual.IsSuccess.Should().BeFalse();
        actual.Error.Code.Should().Be("Pinecone.ReservedFilterField");
    }

    [Fact]
    public void Build_ReservedFilterField_ReturnsValidation()
    {
        // Arrange — directly filtering on the reserved tenant_id key is forbidden.
        var filter = VectorFilter.Eq(MetadataSerializer.TenantMetadataKey, "tenant-1");

        // Act
        var actual = VectorFilterTranslator.Build(filter, null, PineconeTenancyMode.Namespace);

        // Assert
        actual.IsSuccess.Should().BeFalse();
        actual.Error.Code.Should().Be("Pinecone.ReservedFilterField");
    }

    [Fact]
    public void Build_InvalidFieldCharacter_ReturnsValidation()
    {
        // Arrange — a single-quote in the field name is rejected.
        var filter = VectorFilter.Eq("bad'field", "x");

        // Act
        var actual = VectorFilterTranslator.Build(filter, null, PineconeTenancyMode.Namespace);

        // Assert
        actual.IsSuccess.Should().BeFalse();
        actual.Error.Code.Should().Be("Pinecone.InvalidFilterField");
    }

    [Theory]
    [InlineData(VectorFilterKind.And)]
    [InlineData(VectorFilterKind.Or)]
    public void Build_EmptyLogicalGroup_RejectedByFactory(VectorFilterKind kind)
    {
        // Arrange / Act — VectorFilter.And/Or validate non-empty children at construction.
        var act = () => _ = kind == VectorFilterKind.And ? VectorFilter.And() : VectorFilter.Or();

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_ConvertValue_CoercesNumericTypesToLongOrDouble()
    {
        // Arrange — long-coercion path.
        var filterInt = VectorFilter.Eq("a", 42);
        var filterFloat = VectorFilter.Eq("b", 1.5f);
        var filterDecimal = VectorFilter.Eq("c", 3.14m);
        var filterBool = VectorFilter.Eq("d", true);

        // Act
        var aActual = (IDictionary<string, object?>)VectorFilterTranslator
            .Build(filterInt, null, PineconeTenancyMode.Namespace).Value.MetadataFilter!["a"]!;
        var bActual = (IDictionary<string, object?>)VectorFilterTranslator
            .Build(filterFloat, null, PineconeTenancyMode.Namespace).Value.MetadataFilter!["b"]!;
        var cActual = (IDictionary<string, object?>)VectorFilterTranslator
            .Build(filterDecimal, null, PineconeTenancyMode.Namespace).Value.MetadataFilter!["c"]!;
        var dActual = (IDictionary<string, object?>)VectorFilterTranslator
            .Build(filterBool, null, PineconeTenancyMode.Namespace).Value.MetadataFilter!["d"]!;

        // Assert
        aActual["$eq"].Should().Be(42L);
        bActual["$eq"].Should().Be(1.5d);
        cActual["$eq"].Should().BeOfType<double>();
        dActual["$eq"].Should().Be(true);
    }

    [Fact]
    public void Build_ConvertValue_NullValueRejectedByFactory()
    {
        // Arrange / Act — VectorFilter.Eq itself rejects null values; the translator
        // never sees one. Confirm the contract here so the upstream invariant is documented.
        var act = () => VectorFilter.Eq("a", null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("bad'field")]
    [InlineData("contains\nnewline")]
    public void Build_Ne_InvalidField_ReturnsValidation(string field)
    {
        // Arrange
        var filter = VectorFilter.Ne(field, "x");

        // Act
        var actual = VectorFilterTranslator.Build(filter, null, PineconeTenancyMode.Namespace);

        // Assert
        actual.IsSuccess.Should().BeFalse();
        actual.Error.Code.Should().Be("Pinecone.InvalidFilterField");
    }

    [Fact]
    public void Build_In_InvalidField_ReturnsValidation()
    {
        // Arrange
        var filter = VectorFilter.In("bad\"field", new object[] { "a" });

        // Act
        var actual = VectorFilterTranslator.Build(filter, null, PineconeTenancyMode.Namespace);

        // Assert
        actual.IsSuccess.Should().BeFalse();
        actual.Error.Code.Should().Be("Pinecone.InvalidFilterField");
    }

    [Fact]
    public void Build_Range_InvalidField_ReturnsValidation()
    {
        // Arrange
        var filter = VectorFilter.Range("bad\\field", min: 0.0, max: 1.0);

        // Act
        var actual = VectorFilterTranslator.Build(filter, null, PineconeTenancyMode.Namespace);

        // Assert
        actual.IsSuccess.Should().BeFalse();
        actual.Error.Code.Should().Be("Pinecone.InvalidFilterField");
    }

    [Fact]
    public void Build_Range_StringNumeric_Parses()
    {
        // Arrange — strings are coerced via double.Parse.
        var filter = VectorFilter.Range("score", min: "0.5", max: "1.0");

        // Act
        var actual = VectorFilterTranslator.Build(filter, null, PineconeTenancyMode.Namespace);

        // Assert
        actual.IsSuccess.Should().BeTrue();
        var inner = (IDictionary<string, object?>)actual.Value.MetadataFilter!["score"]!;
        inner["$gte"].Should().Be(0.5d);
    }
}
