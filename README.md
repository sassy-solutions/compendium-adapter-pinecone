# `compendium-adapter-pinecone`

Pinecone adapter for the [Compendium](https://github.com/sassy-solutions/compendium) framework. Implements `IVectorStore` from `Compendium.Abstractions.VectorStore` over [Pinecone's](https://www.pinecone.io) managed serverless vector database via a hand-rolled `HttpClient` — no vendor SDK.

Built from [`template-compendium-adapter-dotnet`](https://github.com/sassy-solutions/template-compendium-adapter-dotnet). Companion to [`compendium-adapter-pgvector`](https://github.com/sassy-solutions/compendium-adapter-pgvector) and [`compendium-adapter-qdrant`](https://github.com/sassy-solutions/compendium-adapter-qdrant) — same abstraction, same tenant isolation posture, same Result-pattern error handling.

## What's in this package

| Component | Implements | Purpose |
|---|---|---|
| `PineconeVectorStore` | `IVectorStore` | Embedding storage + ANN similarity search against Pinecone's two-plane REST API |
| `PineconeOptions` | — | API key, control-plane URL, cloud / region, tenancy mode, request timeout |
| `TenantIdentifier` | — | Validates tenant ids against a strict alphanumeric+dash+underscore regex before any wire bind |
| `ServiceCollectionExtensions` | — | DI helpers (`AddCompendiumPinecone(...)`) |

## Install

```bash
dotnet add package Compendium.Adapters.Pinecone
```

## Quick start

Sign up for a free Pinecone account at [app.pinecone.io](https://app.pinecone.io). Free-tier serverless gives you one index in `us-east-1` on AWS — enough to dogfood the round-trip.

```csharp
using Compendium.Abstractions.VectorStore;
using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Pinecone.DependencyInjection;
using Compendium.Adapters.Pinecone.Options;

services.AddCompendiumPinecone(o =>
{
    o.ApiKey = builder.Configuration["Pinecone:ApiKey"]!; // required
    o.Cloud  = PineconeCloud.Aws;
    o.Region = "us-east-1";
});

// IVectorStore is now resolvable from DI.
var store = services.BuildServiceProvider().GetRequiredService<IVectorStore>();
await store.EnsureCollectionAsync("documents", dimension: 1536, DistanceMetric.Cosine);
await store.UpsertAsync("documents", new[]
{
    new VectorRecord("doc-1", embedding, metadata, tenantId: "tenant-1"),
});

var matches = await store.SearchAsync(
    "documents",
    queryEmbedding,
    topK: 5,
    VectorFilter.Eq("category", "support").ForTenant("tenant-1"));
```

A runnable example lives under [`samples/01-managed-rag`](samples/01-managed-rag/Program.cs).

## Configuration options

Bind to the `Compendium:Adapters:Pinecone` section, or pass an inline callback.

| Option | Default | Purpose |
|---|---|---|
| `ApiKey` | _(required)_ | Sent as the `Api-Key` header on every request. |
| `ControlPlaneBaseUrl` | `https://api.pinecone.io` | Pinecone control plane (index lifecycle). |
| `Cloud` | `Aws` | Serverless cloud provider (`Aws` / `Gcp` / `Azure`). |
| `Region` | `us-east-1` | Serverless region. |
| `TenancyMode` | `Namespace` | How tenant ids are propagated to Pinecone (`Namespace` or `Metadata`). |
| `IndexPrefix` | _(empty)_ | Prefix applied to every index name on the wire (e.g. `dev-`, `staging-`). |
| `Timeout` | `30s` | Per-request HTTP timeout. |

## The two-plane architecture (read this once)

Pinecone splits its REST API into two HTTP planes:

| Plane | Host | Used for |
|---|---|---|
| **Control** | `api.pinecone.io` (fixed) | `GET /indexes/{name}`, `POST /indexes` — index lifecycle. |
| **Data** | `<index>-<project>.svc.<region>-<cloud>.pinecone.io` (per-index, returned by control) | `POST /vectors/upsert`, `POST /vectors/delete`, `POST /query` — read/write. |

`PineconeVectorStore` resolves the per-index data-plane host on first use via `GET /indexes/{name}` and caches it in-process. Subsequent reads / writes go straight to the data plane — one extra round-trip on cold start, zero overhead afterwards. `EnsureCollectionAsync` pre-seeds the cache when it succeeds, so the typical flow (`ensure` → `upsert` → `search`) makes exactly one control-plane call.

If you delete an index outside the adapter, the cached host becomes stale — restart the process or let the data-plane 404 propagate as `VectorStore.CollectionNotFound`.

## Tenancy

Pinecone offers two patterns for multi-tenant isolation. The adapter exposes both via `PineconeOptions.TenancyMode`.

### `Namespace` mode (default — recommended)

Each tenant id maps to a Pinecone [namespace](https://docs.pinecone.io/guides/indexes/use-namespaces). Every data-plane call includes a `namespace` field on the request body. This is the cleanest isolation Pinecone offers:

- Reads and writes to one namespace are physically partitioned from another.
- Per-namespace point counts are exposed in Pinecone's stats endpoint.
- No metadata-filter cost on every query.

Use this mode when **tenant cardinality is low to moderate** (Pinecone recommends ≤ 10,000 namespaces per index — past that, namespace overhead starts to matter).

### `Metadata` mode

The tenant id is stored in the record's `metadata` under the reserved `tenant_id` key. Every search emits an `$eq` filter on that key. Use this mode when:

- You have **very high tenant cardinality** (hundreds of thousands of tenants) and namespaces would blow past Pinecone's recommended limits.
- You need cross-tenant searches in addition to per-tenant searches — switching modes per-call isn't supported, but a Metadata-mode index lets you omit the filter to query the whole index.

Trade-off: queries are slower (Pinecone applies the metadata filter on the hot path), and stats don't break down per-tenant.

### Both modes

- `TenantIdentifier.IsValid` rejects anything outside `[a-zA-Z0-9_-]{1,255}` before serialisation — defence-in-depth against tenant-id-driven injection. Mirrors the validator used by [`compendium-adapter-pgvector`](https://github.com/sassy-solutions/compendium-adapter-pgvector) and [`compendium-adapter-qdrant`](https://github.com/sassy-solutions/compendium-adapter-qdrant).
- Tenanted deletes only touch the tenant's vectors. Cross-tenant deletes-by-id are impossible by construction.

## Distance metrics

| `DistanceMetric` | Pinecone `metric` label |
|---|---|
| `Cosine` | `cosine` |
| `L2` | `euclidean` |
| `InnerProduct` | `dotproduct` |

The metric is fixed at `EnsureCollectionAsync` time. Trying to recreate an existing index with a different dimension or metric returns `VectorStore.DimensionMismatch` / `Pinecone.MetricMismatch`.

## Production checklist

- **TLS** — automatic. Every Pinecone endpoint is HTTPS.
- **API key rotation** — never check the API key into source. Rotate via your secret store (we read it via `IOptions<PineconeOptions>`, so any provider works). After rotation you must restart the process: the key is bound to the singleton `HttpClient` on first construction.
- **Region selection** — Pinecone serverless free-tier is `us-east-1`/AWS only. For paid tiers, pick a region close to your application servers; cross-region latency dominates query time for small vectors.
- **Free-tier limits** — one serverless index, 100K vectors / 4 GB write-units / 1M read-units per month. Plenty for prototyping, not enough for prod.
- **Index name rules** — Pinecone limits index names to lowercase `[a-z0-9-]`, 1–45 chars, no leading or trailing dash. The adapter enforces the same rules and returns `Pinecone.InvalidIndex` on violation.
- **Eventual consistency** — Pinecone serverless reads are eventually consistent. Upserts may take a few seconds to be visible to queries. The sample sleeps 3 seconds before searching — adjust for your workload.
- **Pooled `HttpClient`** — the DI extension registers `PineconeVectorStore` via `IHttpClientFactory`, so HTTP connections are pooled across requests by default.
- **Namespace vs metadata trade-off** — start with namespace mode. Switch to metadata mode only when you've measured namespace overhead at scale.

## Versioning

This package is published as `Compendium.Adapters.Pinecone`. Versions are driven by git tags via [MinVer](https://github.com/adamralph/minver) — see [`docs/RELEASE.md`](docs/RELEASE.md). The release tag is set by the orchestrator after merge to `main`.

## Repository conventions

| Aspect | Choice |
|---|---|
| Target | .NET 9, C# 13 |
| HTTP | Hand-rolled `HttpClient` + `System.Text.Json` (camelCase naming policy) |
| Test framework | xUnit 2.9.3 + FluentAssertions 6.12.1 + NSubstitute 5.1.0 |
| HTTP mocking | [`RichardSzalay.MockHttp`](https://github.com/richardszalay/mockhttp) 7.0.0 |
| Integration tests | Live against the real Pinecone API, gated on `PINECONE_API_KEY` (cloud-only — no Testcontainer) |
| Coverage gate | ≥ 90 % line coverage on the unit-testable surface; integration suite covers wire-bound paths |
| Result pattern | `Result<T>` from `Compendium.Core` |

## Build & test locally

```bash
# Unit tests — no Pinecone account required.
dotnet test --filter "FullyQualifiedName!~IntegrationTests"

# Integration tests — needs a Pinecone API key on a free-tier or higher account.
export PINECONE_API_KEY="..."
dotnet test --filter "FullyQualifiedName~IntegrationTests"
```

The integration suite covers idempotent ensure, namespaced upsert/search/delete round-trip, and collection-not-found behaviour against a live Pinecone serverless index. It skips cleanly when `PINECONE_API_KEY` is absent via the `[RequiresPineconeFact]` attribute.

## License

[MIT](LICENSE) — Copyright © 2026 Sassy Solutions.
