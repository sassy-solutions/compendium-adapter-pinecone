// -----------------------------------------------------------------------
// <copyright file="PineconeVectorStore.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Compendium.Abstractions.VectorStore;
using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Pinecone.Internal;
using Compendium.Adapters.Pinecone.Options;
using Compendium.Adapters.Pinecone.Security;
using Compendium.Core.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.Pinecone;

/// <summary>
/// Pinecone-backed <see cref="IVectorStore"/>. Speaks the Pinecone REST API
/// directly (no vendor SDK) against Pinecone's managed serverless vector DB.
/// </summary>
/// <remarks>
/// <para>Two-plane HTTP:</para>
/// <list type="bullet">
///   <item><c>EnsureCollectionAsync</c> hits the <em>control plane</em> at <see cref="PineconeOptions.ControlPlaneBaseUrl"/>.</item>
///   <item>All read/write data calls (<c>Upsert</c>, <c>Delete</c>, <c>Search</c>) hit the <em>data plane</em>: a per-index host returned by the control plane and cached after first use.</item>
/// </list>
/// <para>Tenancy: governed by <see cref="PineconeOptions.TenancyMode"/>:</para>
/// <list type="bullet">
///   <item><see cref="PineconeTenancyMode.Namespace"/> (default) — each tenant id is its own Pinecone <c>namespace</c>.</item>
///   <item><see cref="PineconeTenancyMode.Metadata"/> — tenant id is injected as <c>tenant_id</c> in vector metadata and filtered on read.</item>
/// </list>
/// <para>Every tenant id passes through <see cref="TenantIdentifier.IsValid"/> before hitting the wire.</para>
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Resolved by DI as IVectorStore.")]
public sealed class PineconeVectorStore : IVectorStore
{
    private readonly PineconeOptions _options;
    private readonly PineconeHttpClient _client;
    private readonly ILogger<PineconeVectorStore> _logger;

    /// <summary>
    /// Creates a new <see cref="PineconeVectorStore"/>.
    /// </summary>
    public PineconeVectorStore(
        HttpClient httpClient,
        IOptions<PineconeOptions> options,
        ILogger<PineconeVectorStore> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value ?? throw new ArgumentException("Options.Value is null.", nameof(options));
        _client = new PineconeHttpClient(httpClient, options);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> EnsureCollectionAsync(
        string collection,
        int dimension,
        DistanceMetric metric,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collection))
        {
            return Error.Validation("Pinecone.InvalidIndex", "Index name cannot be null or whitespace.");
        }

        if (dimension <= 0)
        {
            return Error.Validation("Pinecone.InvalidDimension", $"Dimension must be positive, got {dimension}.");
        }

        if (!Enum.IsDefined(metric))
        {
            return Error.Validation(
                "Pinecone.InvalidDistanceMetric",
                $"Distance metric '{metric}' is not a defined DistanceMetric value.");
        }

        var resolved = IndexNaming.Resolve(_options, collection);
        if (!IndexNaming.IsValid(resolved))
        {
            return Error.Validation(
                "Pinecone.InvalidIndex",
                $"Index name '{resolved}' contains characters outside [a-z0-9-], starts/ends with '-', or exceeds {IndexNaming.MaxLength} chars.");
        }

        var label = DistanceMetricMap.Label(metric);

        var existing = await _client
            .ControlGetOptionalAsync<IndexDescription>($"/indexes/{Uri.EscapeDataString(resolved)}", cancellationToken)
            .ConfigureAwait(false);

        if (existing.IsFailure)
        {
            return Result.Failure(existing.Error);
        }

        if (existing.Value is not null)
        {
            if (existing.Value.Dimension != dimension)
            {
                return VectorStoreErrors.DimensionMismatch(dimension, existing.Value.Dimension);
            }

            if (!string.Equals(existing.Value.Metric, label, StringComparison.Ordinal))
            {
                return Error.Conflict(
                    "Pinecone.MetricMismatch",
                    $"Index '{resolved}' already exists with metric '{existing.Value.Metric}', cannot be re-created with '{label}'.");
            }

            if (!string.IsNullOrEmpty(existing.Value.Host))
            {
                _client.CacheHost(resolved, existing.Value.Host);
            }

            _logger.LogDebug("Pinecone index '{Index}' already exists; skipping create.", resolved);
            return Result.Success();
        }

        var body = new CreateIndexRequest
        {
            Name = resolved,
            Dimension = dimension,
            Metric = label,
            Spec = new IndexSpec
            {
                Serverless = new ServerlessSpec
                {
                    Cloud = CloudMap.Label(_options.Cloud),
                    Region = _options.Region,
                },
            },
        };

        var createResult = await _client
            .ControlSendJsonAsync<CreateIndexRequest, IndexDescription>(
                HttpMethod.Post,
                "/indexes",
                body,
                cancellationToken)
            .ConfigureAwait(false);

        if (createResult.IsFailure)
        {
            // 409 Conflict on a concurrent create is benign — treat as success.
            if (string.Equals(createResult.Error.Code, "Pinecone.Conflict", StringComparison.Ordinal))
            {
                _logger.LogDebug(
                    "Pinecone index '{Index}' raced with another writer; treating as ensured.",
                    resolved);
                return Result.Success();
            }

            return Result.Failure(createResult.Error);
        }

        if (!string.IsNullOrEmpty(createResult.Value.Host))
        {
            _client.CacheHost(resolved, createResult.Value.Host);
        }

        _logger.LogInformation(
            "Pinecone index '{Index}' created (dim={Dimension}, metric={Metric}, cloud={Cloud}, region={Region}).",
            resolved,
            dimension,
            label,
            CloudMap.Label(_options.Cloud),
            _options.Region);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> UpsertAsync(
        string collection,
        IReadOnlyList<VectorRecord> records,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collection))
        {
            return Error.Validation("Pinecone.InvalidIndex", "Index name cannot be null or whitespace.");
        }

        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            return Result.Success();
        }

        var resolved = IndexNaming.Resolve(_options, collection);
        if (!IndexNaming.IsValid(resolved))
        {
            return Error.Validation(
                "Pinecone.InvalidIndex",
                $"Index name '{resolved}' contains characters outside [a-z0-9-].");
        }

        // Group records by namespace (namespace-mode: tenant id; metadata-mode: always default).
        // Pinecone's upsert endpoint takes one namespace per call, so we need to fan out.
        var grouped = new Dictionary<string, List<UpsertVector>>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (record is null)
            {
                return Error.Validation("Pinecone.InvalidRecord", "Records cannot contain null entries.");
            }

            if (string.IsNullOrWhiteSpace(record.Id))
            {
                return Error.Validation("Pinecone.InvalidRecordId", "VectorRecord.Id cannot be null or whitespace.");
            }

            if (record.TenantId is not null && !TenantIdentifier.IsValid(record.TenantId))
            {
                return Error.Validation(
                    "Pinecone.InvalidTenantId",
                    $"Record '{record.Id}' has invalid tenant id '{record.TenantId}'.");
            }

            var (ns, injectTenant) = ResolveTenantPlacement(record.TenantId);

            var vector = new UpsertVector
            {
                Id = record.Id,
                Values = record.Embedding.ToArray(),
                Metadata = MetadataSerializer.ToMetadata(record.Metadata, injectTenant),
            };

            var bucketKey = ns ?? string.Empty;
            if (!grouped.TryGetValue(bucketKey, out var bucket))
            {
                bucket = [];
                grouped[bucketKey] = bucket;
            }

            bucket.Add(vector);
        }

        foreach (var (ns, vectors) in grouped)
        {
            var request = new UpsertRequest
            {
                Vectors = vectors,
                Namespace = string.IsNullOrEmpty(ns) ? null : ns,
            };

            var sendResult = await _client
                .DataSendJsonAsync<UpsertRequest, UpsertResponse>(
                    resolved,
                    HttpMethod.Post,
                    "/vectors/upsert",
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

            if (sendResult.IsFailure)
            {
                if (string.Equals(sendResult.Error.Code, "Pinecone.NotFound", StringComparison.Ordinal)
                    || string.Equals(sendResult.Error.Code, "Pinecone.IndexNotFound", StringComparison.Ordinal))
                {
                    return VectorStoreErrors.CollectionNotFound(collection);
                }

                _logger.LogError("Pinecone Upsert failed for '{Index}': {Error}", resolved, sendResult.Error.Message);
                return Result.Failure(sendResult.Error);
            }
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(
        string collection,
        IReadOnlyList<string> ids,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collection))
        {
            return Error.Validation("Pinecone.InvalidIndex", "Index name cannot be null or whitespace.");
        }

        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return Result.Success();
        }

        if (tenantId is not null && !TenantIdentifier.IsValid(tenantId))
        {
            return Error.Validation(
                "Pinecone.InvalidTenantId",
                $"Tenant id '{tenantId}' is not a valid identifier.");
        }

        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return Error.Validation("Pinecone.InvalidRecordId", "Id list cannot contain null or whitespace entries.");
            }
        }

        var resolved = IndexNaming.Resolve(_options, collection);
        if (!IndexNaming.IsValid(resolved))
        {
            return Error.Validation(
                "Pinecone.InvalidIndex",
                $"Index name '{resolved}' contains characters outside [a-z0-9-].");
        }

        var (ns, _) = ResolveTenantPlacement(tenantId);

        var body = new DeleteRequest
        {
            Ids = [.. ids],
            Namespace = ns,
        };

        // In metadata mode, scope the delete with a $eq tenant clause so a stray
        // id from another tenant can't be deleted by mistake.
        if (_options.TenancyMode == PineconeTenancyMode.Metadata && !string.IsNullOrEmpty(tenantId))
        {
            body.Filter = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [MetadataSerializer.TenantMetadataKey] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["$eq"] = tenantId,
                },
            };
        }

        var result = await _client
            .DataSendJsonAsync<DeleteRequest, DeleteResponse>(
                resolved,
                HttpMethod.Post,
                "/vectors/delete",
                body,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            if (string.Equals(result.Error.Code, "Pinecone.NotFound", StringComparison.Ordinal)
                || string.Equals(result.Error.Code, "Pinecone.IndexNotFound", StringComparison.Ordinal))
            {
                return VectorStoreErrors.CollectionNotFound(collection);
            }

            _logger.LogError("Pinecone Delete failed for '{Index}': {Error}", resolved, result.Error.Message);
            return Result.Failure(result.Error);
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<VectorMatch>>> SearchAsync(
        string collection,
        ReadOnlyMemory<float> query,
        int topK,
        VectorFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collection))
        {
            return Error.Validation("Pinecone.InvalidIndex", "Index name cannot be null or whitespace.");
        }

        if (topK <= 0)
        {
            return Error.Validation("Pinecone.InvalidTopK", $"topK must be positive, got {topK}.");
        }

        if (query.Length == 0)
        {
            return Error.Validation("Pinecone.EmptyQueryVector", "Query embedding cannot be empty.");
        }

        var resolved = IndexNaming.Resolve(_options, collection);
        if (!IndexNaming.IsValid(resolved))
        {
            return Error.Validation(
                "Pinecone.InvalidIndex",
                $"Index name '{resolved}' contains characters outside [a-z0-9-].");
        }

        var translated = VectorFilterTranslator.Build(filter, tenantOverride: null, _options.TenancyMode);
        if (translated.IsFailure)
        {
            return Result.Failure<IReadOnlyList<VectorMatch>>(VectorStoreErrors.InvalidFilter(translated.Error.Message));
        }

        var body = new QueryRequest
        {
            Vector = query.ToArray(),
            TopK = topK,
            IncludeMetadata = true,
            IncludeValues = false,
            Namespace = translated.Value.Namespace,
            Filter = translated.Value.MetadataFilter,
        };

        var result = await _client
            .DataSendJsonAsync<QueryRequest, QueryResponse>(
                resolved,
                HttpMethod.Post,
                "/query",
                body,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            if (string.Equals(result.Error.Code, "Pinecone.NotFound", StringComparison.Ordinal)
                || string.Equals(result.Error.Code, "Pinecone.IndexNotFound", StringComparison.Ordinal))
            {
                return VectorStoreErrors.CollectionNotFound(collection);
            }

            _logger.LogError("Pinecone Search failed for '{Index}': {Error}", resolved, result.Error.Message);
            return Result.Failure<IReadOnlyList<VectorMatch>>(result.Error);
        }

        var hits = result.Value.Matches ?? [];
        var matches = new List<VectorMatch>(hits.Count);
        foreach (var hit in hits)
        {
            // In namespace mode the tenant is the response namespace; in metadata
            // mode it's in the metadata payload (and stripped by the serializer).
            var tenant = _options.TenancyMode == PineconeTenancyMode.Namespace
                ? (string.IsNullOrEmpty(result.Value.Namespace) ? null : result.Value.Namespace)
                : MetadataSerializer.ExtractTenantId(hit.Metadata);

            matches.Add(new VectorMatch(
                hit.Id,
                hit.Score,
                MetadataSerializer.FromMetadata(hit.Metadata),
                tenant));
        }

        return Result.Success<IReadOnlyList<VectorMatch>>(matches);
    }

    private (string? Namespace, string? InjectMetadataTenant) ResolveTenantPlacement(string? tenantId)
    {
        if (string.IsNullOrEmpty(tenantId))
        {
            return (null, null);
        }

        return _options.TenancyMode == PineconeTenancyMode.Namespace
            ? (tenantId, null)
            : (null, tenantId);
    }
}
