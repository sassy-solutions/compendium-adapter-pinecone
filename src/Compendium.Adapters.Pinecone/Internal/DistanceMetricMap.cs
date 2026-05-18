// -----------------------------------------------------------------------
// <copyright file="DistanceMetricMap.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.VectorStore.Models;

namespace Compendium.Adapters.Pinecone.Internal;

/// <summary>
/// Translates <see cref="DistanceMetric"/> values into the Pinecone <c>metric</c>
/// string used in index-creation payloads, and back.
/// </summary>
internal static class DistanceMetricMap
{
    /// <summary>
    /// Returns the Pinecone <c>metric</c> label for the given <paramref name="metric"/>.
    /// </summary>
    /// <remarks>
    /// Pinecone supports three labels in <c>POST /indexes</c>:
    /// <list type="bullet">
    ///   <item><c>cosine</c> — cosine similarity (higher is closer).</item>
    ///   <item><c>euclidean</c> — L2 distance (lower is closer).</item>
    ///   <item><c>dotproduct</c> — inner product (higher is closer).</item>
    /// </list>
    /// </remarks>
    public static string Label(DistanceMetric metric) => metric switch
    {
        DistanceMetric.Cosine => "cosine",
        DistanceMetric.L2 => "euclidean",
        DistanceMetric.InnerProduct => "dotproduct",
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unsupported distance metric."),
    };

    /// <summary>
    /// Parses the label produced by <see cref="Label"/> back into a <see cref="DistanceMetric"/>.
    /// Returns <c>false</c> for unknown / unsupported labels.
    /// </summary>
    public static bool TryParseLabel(string? label, out DistanceMetric metric)
    {
        switch (label)
        {
            case "cosine":
                metric = DistanceMetric.Cosine;
                return true;
            case "euclidean":
                metric = DistanceMetric.L2;
                return true;
            case "dotproduct":
                metric = DistanceMetric.InnerProduct;
                return true;
            default:
                metric = default;
                return false;
        }
    }
}
