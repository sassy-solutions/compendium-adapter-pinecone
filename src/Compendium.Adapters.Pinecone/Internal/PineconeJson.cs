// -----------------------------------------------------------------------
// <copyright file="PineconeJson.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Compendium.Adapters.Pinecone.Internal;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> used by the Pinecone adapter.
/// Pinecone's REST API uses camelCase field naming on both sides.
/// </summary>
internal static class PineconeJson
{
    /// <summary>
    /// Serializer options for Pinecone requests + responses. CamelCase naming,
    /// case-insensitive deserialisation, ignore null on write.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}
