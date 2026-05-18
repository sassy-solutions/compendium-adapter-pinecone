// -----------------------------------------------------------------------
// <copyright file="TenantIdentifierTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Pinecone.Security;

namespace Compendium.Adapters.Pinecone.Tests.Security;

public class TenantIdentifierTests
{
    [Theory]
    [InlineData("tenant1")]
    [InlineData("tenant-1")]
    [InlineData("tenant_1")]
    [InlineData("ABC123")]
    [InlineData("a")]
    public void IsValid_AcceptsSafeIds(string id)
    {
        // Arrange / Act
        var actual = TenantIdentifier.IsValid(id);

        // Assert
        actual.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("with space")]
    [InlineData("tenant;DROP TABLE users")]
    [InlineData("tenant'OR'1'='1")]
    [InlineData("tenant\nnewline")]
    [InlineData("with/slash")]
    [InlineData("with.dot")]
    public void IsValid_RejectsUnsafeIds(string? id)
    {
        // Arrange / Act
        var actual = TenantIdentifier.IsValid(id);

        // Assert
        actual.Should().BeFalse();
    }

    [Fact]
    public void IsValid_RejectsOverlyLongId()
    {
        // Arrange
        var id = new string('a', TenantIdentifier.MaxLength + 1);

        // Act
        var actual = TenantIdentifier.IsValid(id);

        // Assert
        actual.Should().BeFalse();
    }

    [Fact]
    public void IsValid_AcceptsMaxLengthId()
    {
        // Arrange
        var id = new string('a', TenantIdentifier.MaxLength);

        // Act
        var actual = TenantIdentifier.IsValid(id);

        // Assert
        actual.Should().BeTrue();
    }
}
