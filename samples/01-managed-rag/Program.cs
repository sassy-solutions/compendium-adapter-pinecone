// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------
//
// Sample 01 — Managed RAG round-trip against Pinecone serverless
// ==============================================================
// Demonstrates the minimal happy path of Compendium.Adapters.Pinecone:
//   1. ensure a 3-dimensional index (cosine distance, serverless),
//   2. upsert five hand-crafted vectors into a tenant namespace,
//   3. search for the three nearest neighbours to a query vector,
//   4. print the matches,
//   5. clean up by deleting the upserted ids.
//
// Connection convention:
//   export PINECONE_API_KEY="your-key"
//   export PINECONE_INDEX="compendium-sample"   # optional
//   export PINECONE_REGION="us-east-1"          # optional
//
// Sign up for a free Pinecone account at https://app.pinecone.io.
// Free-tier serverless gives you one index in us-east-1 (aws) — enough for this sample.

using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Pinecone;
using Compendium.Adapters.Pinecone.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var apiKey = Environment.GetEnvironmentVariable("PINECONE_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("PINECONE_API_KEY env var is required. Sign up at https://app.pinecone.io.");
    return 1;
}

var indexName = Environment.GetEnvironmentVariable("PINECONE_INDEX") ?? "compendium-sample";
var region = Environment.GetEnvironmentVariable("PINECONE_REGION") ?? "us-east-1";

var options = Options.Create(new PineconeOptions
{
    ApiKey = apiKey,
    Cloud = PineconeCloud.Aws,
    Region = region,
    TenancyMode = PineconeTenancyMode.Namespace,
});

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
var logger = loggerFactory.CreateLogger<PineconeVectorStore>();

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var store = new PineconeVectorStore(http, options, logger);

// 1. Ensure the index exists.
var ensure = await store.EnsureCollectionAsync(indexName, dimension: 3, DistanceMetric.Cosine);
if (ensure.IsFailure)
{
    Console.Error.WriteLine($"EnsureCollection failed: {ensure.Error.Code} — {ensure.Error.Message}");
    return 1;
}

// Pinecone indexes take ~30s to become ready on first creation. Allow time for the
// data-plane host to come up before upserting.
await Task.Delay(TimeSpan.FromSeconds(5));

// 2. Upsert five vectors into a tenant namespace.
const string tenant = "demo-tenant";
var records = new List<VectorRecord>
{
    new(Guid.NewGuid().ToString(), new float[] { 1f, 0f, 0f },     new Dictionary<string, object> { ["title"] = "alpha" },  tenant),
    new(Guid.NewGuid().ToString(), new float[] { 0f, 1f, 0f },     new Dictionary<string, object> { ["title"] = "beta" },   tenant),
    new(Guid.NewGuid().ToString(), new float[] { 0f, 0f, 1f },     new Dictionary<string, object> { ["title"] = "gamma" },  tenant),
    new(Guid.NewGuid().ToString(), new float[] { 1f, 1f, 0f },     new Dictionary<string, object> { ["title"] = "ne-x" },   tenant),
    new(Guid.NewGuid().ToString(), new float[] { 0.5f, 0.5f, 0.5f }, new Dictionary<string, object> { ["title"] = "origin" }, tenant),
};

var upsert = await store.UpsertAsync(indexName, records);
if (upsert.IsFailure)
{
    Console.Error.WriteLine($"Upsert failed: {upsert.Error.Code} — {upsert.Error.Message}");
    return 1;
}

await Task.Delay(TimeSpan.FromSeconds(3)); // Pinecone serverless is eventually consistent.

// 3. Search for the three closest to a query that leans toward alpha.
var query = new float[] { 0.9f, 0.1f, 0f };
var search = await store.SearchAsync(indexName, query, topK: 3, filter: VectorFilter.Eq("title", "alpha").ForTenant(tenant));
if (search.IsFailure)
{
    Console.Error.WriteLine($"Search failed: {search.Error.Code} — {search.Error.Message}");
    return 1;
}

// 4. Print results.
Console.WriteLine("Top 3 nearest neighbours (filtered to title=alpha):");
foreach (var match in search.Value!)
{
    var title = match.Metadata.TryGetValue("title", out var t) ? t : "(no title)";
    Console.WriteLine($"  id={match.Id,-40} score={match.Score,8:F4}  title={title}  tenant={match.TenantId}");
}

// 5. Clean up.
var cleanup = await store.DeleteAsync(indexName, records.Select(r => r.Id).ToList(), tenant);
if (cleanup.IsFailure)
{
    Console.Error.WriteLine($"Delete failed: {cleanup.Error.Code} — {cleanup.Error.Message}");
    return 1;
}

return 0;
