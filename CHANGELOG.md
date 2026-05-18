# Changelog

All notable changes to `Compendium.Adapters.Pinecone` are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `PineconeVectorStore` implementing `Compendium.Abstractions.VectorStore.IVectorStore` against the Pinecone REST API via a hand-rolled `HttpClient` (no vendor SDK). Targets Pinecone's managed serverless vector DB across AWS / GCP / Azure regions.
  - `EnsureCollectionAsync` — idempotent. Probes existence via `GET /indexes/{name}` first; rejects dimension or metric drift with structured errors (`VectorStore.DimensionMismatch` / `Pinecone.MetricMismatch`). On create, posts `{ name, dimension, metric, spec: { serverless: { cloud, region } } }` to the control plane and pre-seeds the data-plane host cache from the response. Treats concurrent-create 409s as success.
  - `UpsertAsync` — `POST /vectors/upsert` against the per-index data-plane host. Empty input list is a no-op (no HTTP call). Vectors are grouped by namespace and fanned out in namespace-tenancy mode so each `tenant_id` lands in its own Pinecone namespace.
  - `DeleteAsync` — `POST /vectors/delete`. Untenanted deletes pass the id list directly; in namespace mode a tenanted delete sets the request's `namespace`; in metadata mode it adds a `$eq` tenant filter so a stray id from another tenant can't be deleted by mistake. Cross-tenant deletion is impossible by construction.
  - `SearchAsync` — `POST /query` with `includeMetadata=true`. Maps `VectorFilter` (Eq/Ne/In/Range/And/Or) into Pinecone's MongoDB-style metadata filter (`{ "field": { "$eq": ... } }`, `$ne`, `$in`, `$gt`/`$gte`/`$lt`/`$lte`, `$and`, `$or`). In namespace mode the tenant id moves to the request's `namespace`; in metadata mode it becomes a top-level `$and` clause requiring `tenant_id == X`.
- `PineconeOptions` — API key, control-plane base URL (default `https://api.pinecone.io`), cloud (`Aws` / `Gcp` / `Azure`), region, tenancy mode (`Namespace` default, or `Metadata`), index prefix, request timeout. Data-annotation-validated, `ValidateOnStart`.
- `ServiceCollectionExtensions.AddCompendiumPinecone(...)` — DI registration. Two overloads: `IConfiguration` binding to `Compendium:Adapters:Pinecone` section, or an inline `Action<PineconeOptions>` callback. Uses `IHttpClientFactory` under the hood so HTTP connections are pooled.
- `PineconeHttpClient` — typed HTTP client over Pinecone's **two-plane** architecture. Owns `Api-Key` header injection, camelCase JSON serialisation, per-index data-plane host resolution + in-memory caching, and translation of non-success codes into structured `Result.Failure` (401/403 → `Pinecone.Unauthorized`, 404 → `Pinecone.NotFound`, 408 → `Pinecone.Timeout`, 409 → `Pinecone.Conflict`, 429 → `Pinecone.Throttled`, 5xx → `Pinecone.ServerError`, `HttpRequestException` → `Pinecone.Network`). Captures Pinecone's `{ code, message }` error envelopes and prepends the remote code to the message for easier debugging.
- `TenantIdentifier` — security-hardened tenant id validator (alphanumeric + dash + underscore, ≤ 255 chars). Mirrors `compendium-adapter-qdrant/TenantIdentifier`.
- `IndexNaming` — Pinecone-specific index-name validator (lowercase `[a-z0-9-]`, 1–45 chars, no leading or trailing dash) + configurable prefix resolution.
- `DistanceMetricMap` — `DistanceMetric` ↔ Pinecone metric label (`cosine` / `euclidean` / `dotproduct`).
- `CloudMap` — `PineconeCloud` enum ↔ Pinecone wire string (`aws` / `gcp` / `azure`).
- `MetadataSerializer` — round-trips `IReadOnlyDictionary<string, object>` through Pinecone's JSON `metadata` field. In metadata-tenancy mode the reserved `tenant_id` key is injected on write and stripped on read so callers never see the adapter's bookkeeping. Tolerant `JsonElement` unwrapping for primitive / array / object values.
- `VectorFilterTranslator` — translates `VectorFilter` trees into Pinecone's MongoDB-style filter wire shape. Resolves namespace vs metadata tenant placement based on the configured `PineconeTenancyMode`. Rejects filters that target the reserved `tenant_id` key directly.
- `samples/01-managed-rag` — minimal runnable program that creates an index, upserts five vectors into a tenant namespace, runs a filtered top-3 query, and cleans up.
- `tests/Unit/Compendium.Adapters.Pinecone.Tests` — 195 unit tests covering options validation, tenant id validator (with the same SQL-injection corpus as `compendium-adapter-qdrant`), distance-metric mapping, cloud mapping, index-name validation, vector-filter translation (incl. type-coercion + composite filters + propagation of child failures), metadata round-trip (including `JsonElement` unwrapping), DI registration, and `PineconeVectorStore`'s validation / two-plane HTTP / error-mapping surface via `RichardSzalay.MockHttp` + a custom fault-injecting `HttpMessageHandler`. ≥ 94 % line coverage on the unit-testable surface.
- `tests/Integration/Compendium.Adapters.Pinecone.IntegrationTests` — live round-trip against the real Pinecone API, gated on `PINECONE_API_KEY` via the `[RequiresPineconeFact]` attribute. Covers idempotent ensure, namespaced upsert/search/delete round-trip, and collection-not-found behaviour. Skips cleanly when no credentials are configured (Pinecone is cloud-only — no Testcontainer).

### Dependencies

- `Compendium.Abstractions.VectorStore` 1.0.1
- `Compendium.Abstractions` 1.0.1
- `Compendium.Core` 1.0.1
