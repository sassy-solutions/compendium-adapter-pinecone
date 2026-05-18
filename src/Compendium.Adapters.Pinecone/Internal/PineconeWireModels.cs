// -----------------------------------------------------------------------
// <copyright file="PineconeWireModels.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Pinecone.Internal;

// Wire-format DTOs for the slice of the Pinecone REST API the adapter touches.
// Field naming follows Pinecone's camelCase via the shared JsonSerializerOptions
// (see PineconeJson). Kept internal — callers should never see these.

/// <summary>Body of <c>POST /indexes</c> (control plane).</summary>
internal sealed class CreateIndexRequest
{
    public string Name { get; set; } = string.Empty;

    public int Dimension { get; set; }

    public string Metric { get; set; } = "cosine";

    public IndexSpec Spec { get; set; } = new();
}

/// <summary>The <c>spec</c> envelope of a create-index request.</summary>
internal sealed class IndexSpec
{
    public ServerlessSpec Serverless { get; set; } = new();
}

/// <summary>Serverless cloud / region tuple.</summary>
internal sealed class ServerlessSpec
{
    public string Cloud { get; set; } = "aws";

    public string Region { get; set; } = "us-east-1";
}

/// <summary>Body returned by <c>GET /indexes/{name}</c> (and <c>POST /indexes</c>).</summary>
internal sealed class IndexDescription
{
    public string? Name { get; set; }

    public int Dimension { get; set; }

    public string? Metric { get; set; }

    public string? Host { get; set; }

    public IndexSpec? Spec { get; set; }

    public IndexStatus? Status { get; set; }
}

/// <summary>Lifecycle status of an index.</summary>
internal sealed class IndexStatus
{
    public bool Ready { get; set; }

    public string? State { get; set; }
}

/// <summary>Body of <c>POST /vectors/upsert</c> (data plane).</summary>
internal sealed class UpsertRequest
{
    public List<UpsertVector> Vectors { get; set; } = [];

    public string? Namespace { get; set; }
}

/// <summary>A single vector inside an upsert request.</summary>
internal sealed class UpsertVector
{
    public string Id { get; set; } = string.Empty;

    public float[] Values { get; set; } = [];

    public Dictionary<string, object?>? Metadata { get; set; }
}

/// <summary>Response wrapper of <c>POST /vectors/upsert</c>.</summary>
internal sealed class UpsertResponse
{
    public int UpsertedCount { get; set; }
}

/// <summary>Body of <c>POST /vectors/delete</c>.</summary>
internal sealed class DeleteRequest
{
    public List<string>? Ids { get; set; }

    public bool? DeleteAll { get; set; }

    public string? Namespace { get; set; }

    public Dictionary<string, object?>? Filter { get; set; }
}

/// <summary>Empty success envelope for <c>POST /vectors/delete</c>.</summary>
internal sealed class DeleteResponse
{
    // Pinecone returns "{}" on success — we don't read any fields.
}

/// <summary>Body of <c>POST /query</c>.</summary>
internal sealed class QueryRequest
{
    public float[] Vector { get; set; } = [];

    public int TopK { get; set; }

    public bool IncludeMetadata { get; set; } = true;

    public bool IncludeValues { get; set; }

    public string? Namespace { get; set; }

    public Dictionary<string, object?>? Filter { get; set; }
}

/// <summary>Response of <c>POST /query</c>.</summary>
internal sealed class QueryResponse
{
    public List<QueryMatch>? Matches { get; set; }

    public string? Namespace { get; set; }
}

/// <summary>A single match in <see cref="QueryResponse"/>.</summary>
internal sealed class QueryMatch
{
    public string Id { get; set; } = string.Empty;

    public float Score { get; set; }

    public Dictionary<string, object?>? Metadata { get; set; }
}

/// <summary>Generic Pinecone error envelope (<c>{ "code": "...", "message": "..." }</c>).</summary>
internal sealed class PineconeError
{
    public string? Code { get; set; }

    public string? Message { get; set; }
}
