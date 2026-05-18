// -----------------------------------------------------------------------
// <copyright file="VectorFilterTranslator.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Globalization;
using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Pinecone.Options;
using Compendium.Adapters.Pinecone.Security;
using Compendium.Core.Results;

namespace Compendium.Adapters.Pinecone.Internal;

/// <summary>
/// Translates a <see cref="VectorFilter"/> tree into Pinecone's MongoDB-style
/// filter wire shape (<c>{ "field": { "$eq": v } }</c>).
/// </summary>
/// <remarks>
/// Tenant scope handling depends on the configured <see cref="PineconeTenancyMode"/>:
/// <list type="bullet">
///   <item>
///     <see cref="PineconeTenancyMode.Namespace"/> — the tenant id is passed
///     out-of-band on the request envelope (<c>namespace</c> field). The
///     translator does not inject a tenant clause into the metadata filter.
///   </item>
///   <item>
///     <see cref="PineconeTenancyMode.Metadata"/> — the tenant id is added as
///     a top-level <c>$and</c> clause requiring <c>tenant_id == X</c>.
///   </item>
/// </list>
/// </remarks>
internal static class VectorFilterTranslator
{
    /// <summary>
    /// Builds a Pinecone metadata filter and resolves the wire-level namespace from
    /// the supplied filter + tenant override + tenancy mode.
    /// </summary>
    /// <param name="filter">Caller-supplied filter (may be null).</param>
    /// <param name="tenantOverride">Explicit tenant override; takes precedence over <see cref="VectorFilter.TenantId"/>.</param>
    /// <param name="mode">Tenancy strategy.</param>
    /// <returns>A translated filter + the namespace string Pinecone should use.</returns>
    public static Result<TranslatedFilter> Build(
        VectorFilter? filter,
        string? tenantOverride,
        PineconeTenancyMode mode)
    {
        var tenantId = tenantOverride ?? filter?.TenantId;
        if (!string.IsNullOrEmpty(tenantId) && !TenantIdentifier.IsValid(tenantId))
        {
            return Error.Validation(
                "Pinecone.InvalidTenantId",
                $"Tenant id '{tenantId}' is not a valid identifier (alphanumeric, dashes, underscores; <=255 chars).");
        }

        Dictionary<string, object?>? metadataFilter = null;
        if (filter is not null)
        {
            var translated = TranslateNode(filter);
            if (translated.IsFailure)
            {
                return Result.Failure<TranslatedFilter>(translated.Error);
            }

            metadataFilter = translated.Value;
        }

        // In Metadata mode the tenant scope is part of the filter (top-level $and).
        if (mode == PineconeTenancyMode.Metadata && !string.IsNullOrEmpty(tenantId))
        {
            var tenantClause = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [MetadataSerializer.TenantMetadataKey] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["$eq"] = tenantId,
                },
            };

            metadataFilter = metadataFilter is null
                ? tenantClause
                : new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["$and"] = new List<object?> { tenantClause, metadataFilter },
                };
        }

        var ns = mode == PineconeTenancyMode.Namespace ? tenantId : null;
        return Result.Success(new TranslatedFilter(metadataFilter, ns));
    }

    /// <summary>
    /// Wire-level metadata filter + namespace tuple emitted by <see cref="Build"/>.
    /// </summary>
    /// <param name="MetadataFilter">The metadata filter to send on the request body (null = no filter).</param>
    /// <param name="Namespace">The Pinecone namespace to send on the request body (null = default namespace).</param>
    public readonly record struct TranslatedFilter(Dictionary<string, object?>? MetadataFilter, string? Namespace);

    private static Result<Dictionary<string, object?>> TranslateNode(VectorFilter node)
    {
        switch (node.Kind)
        {
            case VectorFilterKind.Eq:
                {
                    var validation = ValidateField(node.Field);
                    if (validation is not null)
                    {
                        return validation;
                    }

                    return Result.Success(new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [node.Field!] = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["$eq"] = ConvertValue(node.Value),
                        },
                    });
                }

            case VectorFilterKind.Ne:
                {
                    var validation = ValidateField(node.Field);
                    if (validation is not null)
                    {
                        return validation;
                    }

                    return Result.Success(new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [node.Field!] = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["$ne"] = ConvertValue(node.Value),
                        },
                    });
                }

            case VectorFilterKind.In:
                {
                    var validation = ValidateField(node.Field);
                    if (validation is not null)
                    {
                        return validation;
                    }

                    if (node.Values is null || node.Values.Count == 0)
                    {
                        return Error.Validation(
                            "Pinecone.EmptyInFilter",
                            $"In-filter for field '{node.Field}' requires at least one value.");
                    }

                    var values = new List<object?>(node.Values.Count);
                    foreach (var v in node.Values)
                    {
                        values.Add(ConvertValue(v));
                    }

                    return Result.Success(new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [node.Field!] = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["$in"] = values,
                        },
                    });
                }

            case VectorFilterKind.Range:
                {
                    var validation = ValidateField(node.Field);
                    if (validation is not null)
                    {
                        return validation;
                    }

                    if (node.RangeMin is null && node.RangeMax is null)
                    {
                        return Error.Validation(
                            "Pinecone.EmptyRangeFilter",
                            $"Range filter for field '{node.Field}' requires at least one bound.");
                    }

                    var inner = new Dictionary<string, object?>(StringComparer.Ordinal);
                    if (node.RangeMin is not null)
                    {
                        var d = ToDouble(node.RangeMin);
                        inner[node.RangeMinInclusive ? "$gte" : "$gt"] = d;
                    }

                    if (node.RangeMax is not null)
                    {
                        var d = ToDouble(node.RangeMax);
                        inner[node.RangeMaxInclusive ? "$lte" : "$lt"] = d;
                    }

                    return Result.Success(new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [node.Field!] = inner,
                    });
                }

            case VectorFilterKind.And:
            case VectorFilterKind.Or:
                {
                    if (node.Children is null || node.Children.Count == 0)
                    {
                        return Error.Validation(
                            "Pinecone.EmptyLogicalFilter",
                            $"Logical filter '{node.Kind}' requires at least one child.");
                    }

                    var children = new List<object?>(node.Children.Count);
                    foreach (var child in node.Children)
                    {
                        var r = TranslateNode(child);
                        if (r.IsFailure)
                        {
                            return Result.Failure<Dictionary<string, object?>>(r.Error);
                        }

                        children.Add(r.Value);
                    }

                    var op = node.Kind == VectorFilterKind.And ? "$and" : "$or";
                    return Result.Success(new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [op] = children,
                    });
                }

            default:
                return Error.Validation(
                    "Pinecone.UnsupportedFilterKind",
                    $"Filter kind '{node.Kind}' is not supported.");
        }
    }

    private static Result<Dictionary<string, object?>>? ValidateField(string? field)
    {
        if (!IsValidField(field))
        {
            return Error.Validation(
                "Pinecone.InvalidFilterField",
                $"Filter field '{field}' is not a valid metadata key.");
        }

        // Reserved key cannot be filtered on directly (in metadata-tenancy mode the
        // adapter injects it itself; in namespace mode it isn't even in the payload).
        if (string.Equals(field, MetadataSerializer.TenantMetadataKey, StringComparison.Ordinal))
        {
            return Error.Validation(
                "Pinecone.ReservedFilterField",
                $"Filter field '{field}' is reserved by the adapter.");
        }

        return null;
    }

    private static object? ConvertValue(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b,
        int i => (long)i,
        long l => l,
        float f => (double)f,
        double d => d,
        decimal m => (double)m,
        _ => value.ToString(),
    };

    private static double ToDouble(object value)
    {
        return value switch
        {
            int i => i,
            long l => l,
            float f => f,
            double d => d,
            decimal m => (double)m,
            string s => double.Parse(s, CultureInfo.InvariantCulture),
            _ => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        };
    }

    private static bool IsValidField(string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return false;
        }

        foreach (var c in field)
        {
            if (c is '\'' or '"' or '\\' or '\n' or '\r' or '\t' or '\0')
            {
                return false;
            }
        }

        return field.Length <= 128;
    }
}
