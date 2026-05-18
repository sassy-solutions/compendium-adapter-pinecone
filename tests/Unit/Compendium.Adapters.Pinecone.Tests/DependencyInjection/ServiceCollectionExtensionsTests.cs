// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensionsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.VectorStore;
using Compendium.Adapters.Pinecone.DependencyInjection;
using Compendium.Adapters.Pinecone.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.Pinecone.Tests.DependencyInjection;

/// <summary>
/// DI registration semantics for the Pinecone adapter — verifies binding,
/// IVectorStore resolution, and null-argument guards.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCompendiumPinecone_WithConfiguration_BindsAndRegistersIVectorStore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Compendium:Adapters:Pinecone:ApiKey"] = "k1",
                ["Compendium:Adapters:Pinecone:ControlPlaneBaseUrl"] = "https://api.pinecone.io",
                ["Compendium:Adapters:Pinecone:Region"] = "us-east-1",
            })
            .Build();

        // Act
        var actual = services.AddCompendiumPinecone(configuration);
        var sp = actual.BuildServiceProvider();

        // Assert
        actual.Should().BeSameAs(services);
        sp.GetRequiredService<IVectorStore>().Should().BeOfType<PineconeVectorStore>();
        sp.GetRequiredService<IOptions<PineconeOptions>>().Value.ApiKey.Should().Be("k1");
    }

    [Fact]
    public void AddCompendiumPinecone_WithCallback_RegistersIVectorStore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddCompendiumPinecone(o =>
        {
            o.ApiKey = "k1";
            o.Region = "us-east-1";
        });
        var sp = services.BuildServiceProvider();

        // Assert
        sp.GetRequiredService<IVectorStore>().Should().BeOfType<PineconeVectorStore>();
    }

    [Fact]
    public void AddCompendiumPinecone_NullServicesWithConfiguration_Throws()
    {
        // Arrange
        IServiceCollection? services = null;
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var act = () => services!.AddCompendiumPinecone(configuration);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddCompendiumPinecone_NullServicesWithCallback_Throws()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act
        var act = () => services!.AddCompendiumPinecone(_ => { });

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddCompendiumPinecone_NullConfiguration_Throws()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddCompendiumPinecone((IConfiguration)null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddCompendiumPinecone_NullCallback_Throws()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddCompendiumPinecone((Action<PineconeOptions>)null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
