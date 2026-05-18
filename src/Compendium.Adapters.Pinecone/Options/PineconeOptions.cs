// -----------------------------------------------------------------------
// <copyright file="PineconeOptions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;

namespace Compendium.Adapters.Pinecone.Options;

/// <summary>
/// How a tenant id is propagated to Pinecone on the wire.
/// </summary>
public enum PineconeTenancyMode
{
    /// <summary>
    /// Each tenant id maps to a Pinecone <c>namespace</c> on every data-plane call
    /// (recommended for low-cardinality tenants — cleanest isolation, fastest queries).
    /// </summary>
    Namespace = 0,

    /// <summary>
    /// Tenant id is stored in the record's <c>metadata</c> under the reserved
    /// <c>tenant_id</c> key and filtered with a <c>$eq</c> clause on every search
    /// (use for very high tenant cardinality where a namespace-per-tenant would explode).
    /// </summary>
    Metadata = 1,
}

/// <summary>
/// Cloud provider hosting a Pinecone serverless index.
/// </summary>
public enum PineconeCloud
{
    /// <summary>Amazon Web Services (default).</summary>
    Aws = 0,

    /// <summary>Google Cloud Platform.</summary>
    Gcp = 1,

    /// <summary>Microsoft Azure.</summary>
    Azure = 2,
}

/// <summary>
/// Configuration for <see cref="PineconeVectorStore"/>.
/// Bound from <c>Compendium:Adapters:Pinecone</c> by default.
/// </summary>
public sealed class PineconeOptions
{
    /// <summary>
    /// Configuration section name used by <c>IConfiguration.GetSection(...)</c>.
    /// </summary>
    public const string SectionName = "Compendium:Adapters:Pinecone";

    /// <summary>
    /// Pinecone API key sent as the <c>Api-Key</c> header on every request. Required.
    /// </summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Pinecone control-plane base URL. Used for index lifecycle calls
    /// (<c>GET /indexes/{name}</c>, <c>POST /indexes</c>). Default
    /// <c>https://api.pinecone.io</c>.
    /// </summary>
    [Required]
    [Url]
    public string ControlPlaneBaseUrl { get; set; } = "https://api.pinecone.io";

    /// <summary>
    /// Serverless cloud provider used when creating new indexes. Default
    /// <see cref="PineconeCloud.Aws"/>.
    /// </summary>
    public PineconeCloud Cloud { get; set; } = PineconeCloud.Aws;

    /// <summary>
    /// Serverless region used when creating new indexes (e.g. <c>us-east-1</c>,
    /// <c>us-west-2</c>, <c>eu-west-1</c>). Default <c>us-east-1</c>.
    /// </summary>
    [Required]
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// How tenant ids are propagated to Pinecone on the wire.
    /// Default <see cref="PineconeTenancyMode.Namespace"/>.
    /// </summary>
    public PineconeTenancyMode TenancyMode { get; set; } = PineconeTenancyMode.Namespace;

    /// <summary>
    /// Optional prefix applied to every index name. Default empty.
    /// Useful when one Pinecone project is shared across environments
    /// (e.g. <c>dev-</c>, <c>staging-</c>).
    /// </summary>
    public string IndexPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Per-request timeout. Default 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
