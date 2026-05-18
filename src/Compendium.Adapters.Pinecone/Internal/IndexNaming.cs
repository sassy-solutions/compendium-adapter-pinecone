// -----------------------------------------------------------------------
// <copyright file="IndexNaming.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.RegularExpressions;
using Compendium.Adapters.Pinecone.Options;

namespace Compendium.Adapters.Pinecone.Internal;

/// <summary>
/// Helpers for deriving and validating Pinecone index names from the
/// logical names used by callers.
/// </summary>
/// <remarks>
/// Pinecone restricts index names to lowercase alphanumerics + dashes, no
/// underscores, no uppercase, max 45 characters. We accept the slightly
/// wider <c>[a-z0-9-]</c> set (Pinecone's real rule) but keep our own posture
/// strict so a typo never reaches the wire.
/// </remarks>
internal static partial class IndexNaming
{
    [GeneratedRegex(@"^[a-z0-9-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IndexRegex();

    /// <summary>
    /// Maximum allowed length of a (prefixed) index name on the wire. Pinecone
    /// rejects names longer than 45 chars at the control plane.
    /// </summary>
    public const int MaxLength = 45;

    /// <summary>
    /// Returns whether the supplied index name passes the safe-character regex
    /// and length cap.
    /// </summary>
    public static bool IsValid(string? index)
    {
        if (string.IsNullOrEmpty(index))
        {
            return false;
        }

        if (index.Length > MaxLength)
        {
            return false;
        }

        // Pinecone rejects names starting or ending with a dash.
        if (index[0] == '-' || index[^1] == '-')
        {
            return false;
        }

        return IndexRegex().IsMatch(index);
    }

    /// <summary>
    /// Returns the resolved index name (configured prefix + caller-supplied index).
    /// </summary>
    public static string Resolve(PineconeOptions options, string index)
    {
        ArgumentNullException.ThrowIfNull(options);
        return (options.IndexPrefix ?? string.Empty) + index;
    }
}
