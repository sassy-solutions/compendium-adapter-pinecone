// -----------------------------------------------------------------------
// <copyright file="CloudMapTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Pinecone.Internal;
using Compendium.Adapters.Pinecone.Options;

namespace Compendium.Adapters.Pinecone.Tests.Internal;

public class CloudMapTests
{
    [Theory]
    [InlineData(PineconeCloud.Aws, "aws")]
    [InlineData(PineconeCloud.Gcp, "gcp")]
    [InlineData(PineconeCloud.Azure, "azure")]
    public void Label_MapsKnownCloudsToLowercase(PineconeCloud cloud, string expected)
    {
        // Arrange / Act
        var actual = CloudMap.Label(cloud);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void Label_UnknownCloud_Throws()
    {
        // Arrange
        var bogus = (PineconeCloud)999;

        // Act
        var act = () => CloudMap.Label(bogus);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
