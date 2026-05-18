// -----------------------------------------------------------------------
// <copyright file="PineconeOptionsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using Compendium.Adapters.Pinecone.Options;

namespace Compendium.Adapters.Pinecone.Tests.Options;

/// <summary>
/// Verifies the configurable surface of <see cref="PineconeOptions"/> — defaults,
/// data-annotation validation, and the public section-name constant.
/// </summary>
public class PineconeOptionsTests
{
    [Fact]
    public void Defaults_AreSensibleForServerlessCloud()
    {
        // Arrange / Act
        var options = new PineconeOptions();

        // Assert
        options.ControlPlaneBaseUrl.Should().Be("https://api.pinecone.io");
        options.ApiKey.Should().BeEmpty();
        options.Cloud.Should().Be(PineconeCloud.Aws);
        options.Region.Should().Be("us-east-1");
        options.TenancyMode.Should().Be(PineconeTenancyMode.Namespace);
        options.IndexPrefix.Should().BeEmpty();
        options.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData("https://api.pinecone.io", "k", "us-east-1", true)]
    [InlineData("https://controller.pinecone.io", "k", "eu-west-1", true)]
    [InlineData("", "k", "us-east-1", false)]
    [InlineData("not-a-url", "k", "us-east-1", false)]
    [InlineData("https://api.pinecone.io", "", "us-east-1", false)]
    [InlineData("https://api.pinecone.io", "k", "", false)]
    public void DataAnnotations_ValidateAsExpected(string controlPlane, string apiKey, string region, bool expectedValid)
    {
        // Arrange
        var options = new PineconeOptions
        {
            ControlPlaneBaseUrl = controlPlane,
            ApiKey = apiKey,
            Region = region,
        };
        var ctx = new ValidationContext(options);
        var results = new List<ValidationResult>();

        // Act
        var actual = Validator.TryValidateObject(options, ctx, results, validateAllProperties: true);

        // Assert
        actual.Should().Be(expectedValid);
    }

    [Fact]
    public void SectionName_IsCanonical()
    {
        // Assert
        PineconeOptions.SectionName.Should().Be("Compendium:Adapters:Pinecone");
    }

    [Theory]
    [InlineData(PineconeCloud.Aws)]
    [InlineData(PineconeCloud.Gcp)]
    [InlineData(PineconeCloud.Azure)]
    public void Cloud_AcceptsAllDefinedValues(PineconeCloud cloud)
    {
        // Arrange / Act
        var options = new PineconeOptions { Cloud = cloud };

        // Assert
        options.Cloud.Should().Be(cloud);
    }

    [Theory]
    [InlineData(PineconeTenancyMode.Namespace)]
    [InlineData(PineconeTenancyMode.Metadata)]
    public void TenancyMode_AcceptsAllDefinedValues(PineconeTenancyMode mode)
    {
        // Arrange / Act
        var options = new PineconeOptions { TenancyMode = mode };

        // Assert
        options.TenancyMode.Should().Be(mode);
    }
}
