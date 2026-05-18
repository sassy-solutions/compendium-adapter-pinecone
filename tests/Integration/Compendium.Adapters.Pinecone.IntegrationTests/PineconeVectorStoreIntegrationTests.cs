// -----------------------------------------------------------------------
// <copyright file="PineconeVectorStoreIntegrationTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Pinecone;
using Compendium.Adapters.Pinecone.IntegrationTests.Fixtures;
using Compendium.Adapters.Pinecone.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compendium.Adapters.Pinecone.IntegrationTests;

/// <summary>
/// Live round-trip against a Pinecone serverless index. Skips when
/// <c>PINECONE_API_KEY</c> is not set. Pinecone is cloud-only — no
/// container fixture, just a free-tier index.
/// </summary>
/// <remarks>
/// Environment variables consumed:
/// <list type="bullet">
///   <item><c>PINECONE_API_KEY</c> — required. Skips the suite if absent.</item>
///   <item><c>PINECONE_INDEX</c> — optional. Test index name (default <c>compendium-it</c>).</item>
///   <item><c>PINECONE_CLOUD</c> — optional. <c>aws</c> / <c>gcp</c> / <c>azure</c> (default <c>aws</c>).</item>
///   <item><c>PINECONE_REGION</c> — optional. Default <c>us-east-1</c>.</item>
/// </list>
/// </remarks>
public class PineconeVectorStoreIntegrationTests
{
    private static PineconeVectorStore CreateStore()
    {
        var apiKey = Environment.GetEnvironmentVariable("PINECONE_API_KEY")!;
        var cloud = Environment.GetEnvironmentVariable("PINECONE_CLOUD") switch
        {
            "gcp" => PineconeCloud.Gcp,
            "azure" => PineconeCloud.Azure,
            _ => PineconeCloud.Aws,
        };
        var region = Environment.GetEnvironmentVariable("PINECONE_REGION") ?? "us-east-1";

        var options = Microsoft.Extensions.Options.Options.Create(new PineconeOptions
        {
            ApiKey = apiKey,
            Cloud = cloud,
            Region = region,
        });

        return new PineconeVectorStore(
            new HttpClient { Timeout = TimeSpan.FromSeconds(60) },
            options,
            NullLogger<PineconeVectorStore>.Instance);
    }

    private static string IndexName =>
        Environment.GetEnvironmentVariable("PINECONE_INDEX") ?? "compendium-it";

    [RequiresPineconeFact]
    public async Task EnsureCollection_IsIdempotent()
    {
        // Arrange
        var store = CreateStore();
        var index = IndexName;

        // Act — call twice; second should be a no-op.
        var first = await store.EnsureCollectionAsync(index, dimension: 3, DistanceMetric.Cosine);
        var second = await store.EnsureCollectionAsync(index, dimension: 3, DistanceMetric.Cosine);

        // Assert
        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
    }

    [RequiresPineconeFact]
    public async Task UpsertSearchDelete_RoundTrip_NamespaceTenancy()
    {
        // Arrange — namespace mode (default).
        var store = CreateStore();
        var index = IndexName;
        var tenant = $"tenant-{Guid.NewGuid():N}"[..32];

        await store.EnsureCollectionAsync(index, dimension: 3, DistanceMetric.Cosine);

        var records = new List<VectorRecord>
        {
            new("alpha", new float[] { 1f, 0f, 0f }, new Dictionary<string, object> { ["title"] = "a" }, tenant),
            new("beta",  new float[] { 0f, 1f, 0f }, new Dictionary<string, object> { ["title"] = "b" }, tenant),
        };

        try
        {
            // Act — upsert + read back.
            var upsert = await store.UpsertAsync(index, records);
            upsert.IsSuccess.Should().BeTrue();

            // Pinecone has eventual consistency; allow a short cushion.
            await Task.Delay(TimeSpan.FromSeconds(5));

            var search = await store.SearchAsync(
                index,
                new float[] { 0.9f, 0.1f, 0f },
                topK: 2,
                VectorFilter.Eq("title", "a").ForTenant(tenant));

            // Assert
            search.IsSuccess.Should().BeTrue();
            search.Value.Should().NotBeEmpty();
            search.Value![0].TenantId.Should().Be(tenant);
        }
        finally
        {
            await store.DeleteAsync(index, records.Select(r => r.Id).ToList(), tenant);
        }
    }

    [RequiresPineconeFact]
    public async Task Search_NonExistentIndex_ReturnsCollectionNotFound()
    {
        // Arrange
        var store = CreateStore();
        var bogus = $"does-not-exist-{Guid.NewGuid():N}"[..30];

        // Act
        var result = await store.SearchAsync(bogus, new float[] { 1, 2, 3 }, 1);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VectorStore.CollectionNotFound");
    }
}
