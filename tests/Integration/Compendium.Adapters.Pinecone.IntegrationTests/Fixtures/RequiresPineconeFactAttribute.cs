// -----------------------------------------------------------------------
// <copyright file="RequiresPineconeFactAttribute.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Xunit;

namespace Compendium.Adapters.Pinecone.IntegrationTests.Fixtures;

/// <summary>
/// A <c>[Fact]</c> that auto-skips when the <c>PINECONE_API_KEY</c> environment variable
/// is not set. Pinecone is cloud-only; there is no Testcontainer for it.
/// </summary>
public sealed class RequiresPineconeFactAttribute : FactAttribute
{
    /// <summary>
    /// Creates a fact that skips itself when no Pinecone credentials are configured.
    /// </summary>
    public RequiresPineconeFactAttribute()
    {
        var key = Environment.GetEnvironmentVariable("PINECONE_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Skip = "Pinecone integration tests require PINECONE_API_KEY to be set.";
        }
    }
}
