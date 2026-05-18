// -----------------------------------------------------------------------
// <copyright file="IndexNamingTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Pinecone.Internal;
using Compendium.Adapters.Pinecone.Options;

namespace Compendium.Adapters.Pinecone.Tests.Internal;

public class IndexNamingTests
{
    [Theory]
    [InlineData("documents")]
    [InlineData("my-index")]
    [InlineData("a")]
    [InlineData("idx-1-2-3")]
    public void IsValid_AcceptsSafeNames(string name)
    {
        // Arrange / Act
        var actual = IndexNaming.IsValid(name);

        // Assert
        actual.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("With_Underscore")] // underscores not allowed
    [InlineData("UPPER")]
    [InlineData("with space")]
    [InlineData("with/slash")]
    [InlineData("with;semi")]
    [InlineData("-leading-dash")]
    [InlineData("trailing-dash-")]
    public void IsValid_RejectsUnsafeNames(string? name)
    {
        // Arrange / Act
        var actual = IndexNaming.IsValid(name);

        // Assert
        actual.Should().BeFalse();
    }

    [Fact]
    public void IsValid_RejectsNameLongerThanMax()
    {
        // Arrange
        var name = new string('a', IndexNaming.MaxLength + 1);

        // Act
        var actual = IndexNaming.IsValid(name);

        // Assert
        actual.Should().BeFalse();
    }

    [Fact]
    public void IsValid_AcceptsMaxLengthName()
    {
        // Arrange
        var name = new string('a', IndexNaming.MaxLength);

        // Act
        var actual = IndexNaming.IsValid(name);

        // Assert
        actual.Should().BeTrue();
    }

    [Fact]
    public void Resolve_NoPrefix_ReturnsIndexAsIs()
    {
        // Arrange
        var options = new PineconeOptions { ApiKey = "k", IndexPrefix = string.Empty };

        // Act
        var actual = IndexNaming.Resolve(options, "documents");

        // Assert
        actual.Should().Be("documents");
    }

    [Fact]
    public void Resolve_WithPrefix_PrependsPrefix()
    {
        // Arrange
        var options = new PineconeOptions { ApiKey = "k", IndexPrefix = "dev-" };

        // Act
        var actual = IndexNaming.Resolve(options, "documents");

        // Assert
        actual.Should().Be("dev-documents");
    }

    [Fact]
    public void Resolve_NullOptions_Throws()
    {
        // Arrange / Act
        var act = () => IndexNaming.Resolve(null!, "documents");

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
