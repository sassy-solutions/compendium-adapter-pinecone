// -----------------------------------------------------------------------
// <copyright file="MetadataSerializer.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;

namespace Compendium.Adapters.Pinecone.Internal;

/// <summary>
/// Round-trips a <see cref="IReadOnlyDictionary{TKey, TValue}"/> of metadata
/// through Pinecone's JSON <c>metadata</c> field.
/// </summary>
internal static class MetadataSerializer
{
    /// <summary>
    /// The reserved metadata key used to scope vectors to a tenant in
    /// <see cref="Compendium.Adapters.Pinecone.Options.PineconeTenancyMode.Metadata"/>
    /// mode. Mirrors the qdrant adapter's key.
    /// </summary>
    public const string TenantMetadataKey = "tenant_id";

    /// <summary>
    /// Materialises a metadata dictionary suitable for the Pinecone <c>metadata</c> field.
    /// Returns null when <paramref name="metadata"/> is null or empty and no tenant id is to be injected.
    /// </summary>
    /// <param name="metadata">Caller metadata.</param>
    /// <param name="injectTenantId">If non-null, injected as <c>tenant_id</c>.</param>
    public static Dictionary<string, object?>? ToMetadata(
        IReadOnlyDictionary<string, object>? metadata,
        string? injectTenantId)
    {
        if ((metadata is null || metadata.Count == 0) && string.IsNullOrEmpty(injectTenantId))
        {
            return null;
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (metadata is not null)
        {
            foreach (var kvp in metadata)
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        if (!string.IsNullOrEmpty(injectTenantId))
        {
            result[TenantMetadataKey] = injectTenantId;
        }

        return result;
    }

    /// <summary>
    /// Converts a Pinecone metadata payload back into a caller-facing dictionary.
    /// <c>JsonElement</c> values (which is what System.Text.Json deserialises
    /// <c>object</c> to) are unwrapped into native .NET types. The reserved
    /// <c>tenant_id</c> key is stripped so callers see the metadata they
    /// originally upserted, not the adapter's bookkeeping.
    /// </summary>
    public static IReadOnlyDictionary<string, object> FromMetadata(IReadOnlyDictionary<string, object?>? metadata)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        if (metadata is null || metadata.Count == 0)
        {
            return result;
        }

        foreach (var kvp in metadata)
        {
            if (string.Equals(kvp.Key, TenantMetadataKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (kvp.Value is null)
            {
                continue;
            }

            result[kvp.Key] = Unwrap(kvp.Value);
        }

        return result;
    }

    /// <summary>
    /// Extracts the tenant id from a Pinecone metadata payload, if present.
    /// </summary>
    public static string? ExtractTenantId(IReadOnlyDictionary<string, object?>? metadata)
    {
        if (metadata is null || !metadata.TryGetValue(TenantMetadataKey, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            string s => s,
            JsonElement el when el.ValueKind == JsonValueKind.String => el.GetString(),
            _ => raw.ToString(),
        };
    }

    private static object Unwrap(object value) => value switch
    {
        JsonElement element => UnwrapElement(element),
        _ => value,
    };

    private static object UnwrapElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.TryGetInt64(out var l)
            ? l
            : (element.TryGetDouble(out var d) ? d : (object)element.GetDecimal()),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Array => element.EnumerateArray().Select(UnwrapElement).ToArray(),
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => UnwrapElement(p.Value)),
        _ => element.ToString(),
    };
}
