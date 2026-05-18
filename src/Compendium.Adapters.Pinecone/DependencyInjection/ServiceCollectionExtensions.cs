// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.VectorStore;
using Compendium.Adapters.Pinecone.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.Pinecone.DependencyInjection;

/// <summary>
/// DI registration helpers for the Pinecone adapter.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PineconeVectorStore"/> as <see cref="IVectorStore"/> bound to
    /// <see cref="PineconeOptions.SectionName"/>.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <param name="configuration">Source configuration; section <see cref="PineconeOptions.SectionName"/> is bound.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddCompendiumPinecone(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PineconeOptions>()
            .Bind(configuration.GetSection(PineconeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        RegisterStore(services);
        return services;
    }

    /// <summary>
    /// Registers <see cref="PineconeVectorStore"/> as <see cref="IVectorStore"/> with an inline
    /// configuration callback.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <param name="configure">Callback to mutate <see cref="PineconeOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddCompendiumPinecone(
        this IServiceCollection services,
        Action<PineconeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<PineconeOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        RegisterStore(services);
        return services;
    }

    private static void RegisterStore(IServiceCollection services)
    {
        services.AddHttpClient<PineconeVectorStore>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<PineconeOptions>>().Value;
            client.Timeout = options.Timeout;
            if (!string.IsNullOrEmpty(options.ApiKey))
            {
                client.DefaultRequestHeaders.Add("Api-Key", options.ApiKey);
            }
        });

        services.AddSingleton<IVectorStore>(sp => sp.GetRequiredService<PineconeVectorStore>());
    }
}
