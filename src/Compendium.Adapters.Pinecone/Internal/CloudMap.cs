// -----------------------------------------------------------------------
// <copyright file="CloudMap.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Pinecone.Options;

namespace Compendium.Adapters.Pinecone.Internal;

/// <summary>
/// Maps <see cref="PineconeCloud"/> enum values to the lowercase wire strings
/// expected by the Pinecone control plane's <c>spec.serverless.cloud</c> field.
/// </summary>
internal static class CloudMap
{
    /// <summary>
    /// Returns the Pinecone serverless cloud label (<c>aws</c> / <c>gcp</c> / <c>azure</c>).
    /// </summary>
    public static string Label(PineconeCloud cloud) => cloud switch
    {
        PineconeCloud.Aws => "aws",
        PineconeCloud.Gcp => "gcp",
        PineconeCloud.Azure => "azure",
        _ => throw new ArgumentOutOfRangeException(nameof(cloud), cloud, "Unsupported cloud."),
    };
}
