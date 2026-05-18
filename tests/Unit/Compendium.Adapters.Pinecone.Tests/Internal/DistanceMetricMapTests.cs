// -----------------------------------------------------------------------
// <copyright file="DistanceMetricMapTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Pinecone.Internal;

namespace Compendium.Adapters.Pinecone.Tests.Internal;

public class DistanceMetricMapTests
{
    [Theory]
    [InlineData(DistanceMetric.Cosine, "cosine")]
    [InlineData(DistanceMetric.L2, "euclidean")]
    [InlineData(DistanceMetric.InnerProduct, "dotproduct")]
    public void Label_MapsKnownMetricsToPineconeLabels(DistanceMetric metric, string expected)
    {
        // Arrange / Act
        var actual = DistanceMetricMap.Label(metric);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void Label_UnknownMetric_Throws()
    {
        // Arrange
        var bogus = (DistanceMetric)999;

        // Act
        var act = () => DistanceMetricMap.Label(bogus);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("cosine", DistanceMetric.Cosine, true)]
    [InlineData("euclidean", DistanceMetric.L2, true)]
    [InlineData("dotproduct", DistanceMetric.InnerProduct, true)]
    public void TryParseLabel_KnownLabel_RoundTrips(string label, DistanceMetric expected, bool expectedReturn)
    {
        // Arrange / Act
        var actualReturn = DistanceMetricMap.TryParseLabel(label, out var actual);

        // Assert
        actualReturn.Should().Be(expectedReturn);
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("manhattan")]
    [InlineData("Cosine")] // case-sensitive — Pinecone emits "cosine"
    public void TryParseLabel_UnknownLabel_ReturnsFalse(string? label)
    {
        // Arrange / Act
        var actualReturn = DistanceMetricMap.TryParseLabel(label, out _);

        // Assert
        actualReturn.Should().BeFalse();
    }
}
