// -----------------------------------------------------------------------
// <copyright file="PineconeVectorStoreErrorPathTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Pinecone.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compendium.Adapters.Pinecone.Tests;

/// <summary>
/// HTTP-layer error-handling for <see cref="PineconeVectorStore"/> driven by a
/// fault-injecting <see cref="HttpMessageHandler"/>. Exercises the network /
/// timeout exception branches that <see cref="RichardSzalay.MockHttp.MockHttpMessageHandler"/>
/// can't easily simulate.
/// </summary>
public class PineconeVectorStoreErrorPathTests
{
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public required Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> OnSend { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => OnSend(request, cancellationToken);
    }

    private static PineconeVectorStore CreateStore(ThrowingHandler handler)
    {
        var http = new HttpClient(handler);
        var opts = Microsoft.Extensions.Options.Options.Create(new PineconeOptions { ApiKey = "k" });
        return new PineconeVectorStore(http, opts, NullLogger<PineconeVectorStore>.Instance);
    }

    [Fact]
    public async Task EnsureCollectionAsync_HttpRequestException_MapsToNetworkError()
    {
        // Arrange
        var store = CreateStore(new ThrowingHandler
        {
            OnSend = (_, _) => throw new HttpRequestException("connection refused"),
        });

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.Network");
    }

    [Fact]
    public async Task EnsureCollectionAsync_TaskCanceledNotByUser_MapsToTimeout()
    {
        // Arrange
        var store = CreateStore(new ThrowingHandler
        {
            OnSend = (_, _) => throw new TaskCanceledException("request timed out"),
        });

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.Timeout");
    }

    [Fact]
    public async Task EnsureCollectionAsync_CreatePathTaskCanceled_MapsToTimeout()
    {
        // Arrange — first call (GET) returns 404; second call (POST /indexes) times out.
        var hits = 0;
        var store = CreateStore(new ThrowingHandler
        {
            OnSend = (req, _) =>
            {
                hits++;
                if (hits == 1)
                {
                    return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                    {
                        Content = new StringContent("{}"),
                    });
                }

                throw new TaskCanceledException("timed out");
            },
        });

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.Timeout");
    }

    [Fact]
    public async Task SearchAsync_HttpRequestException_MapsToNetworkError()
    {
        // Arrange
        var store = CreateStore(new ThrowingHandler
        {
            OnSend = (_, _) => throw new HttpRequestException("connection refused"),
        });

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.Network");
    }

    [Fact]
    public async Task UpsertAsync_HttpRequestException_MapsToNetworkError()
    {
        // Arrange
        var store = CreateStore(new ThrowingHandler
        {
            OnSend = (_, _) => throw new HttpRequestException("dial tcp: refused"),
        });
        var records = new List<VectorRecord>
        {
            new("id1", new float[] { 1, 2, 3 }, new Dictionary<string, object>()),
        };

        // Act
        var result = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.Network");
    }

    [Fact]
    public async Task DeleteAsync_HttpRequestException_MapsToNetworkError()
    {
        // Arrange
        var store = CreateStore(new ThrowingHandler
        {
            OnSend = (_, _) => throw new HttpRequestException("eof"),
        });

        // Act
        var result = await store.DeleteAsync("documents", new List<string> { "a" }, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.Network");
    }

    [Fact]
    public async Task UpsertAsync_DataPlaneNetworkErrorAfterDescribe_PropagatesError()
    {
        // Arrange — GET succeeds, POST throws.
        var hits = 0;
        var store = CreateStore(new ThrowingHandler
        {
            OnSend = (req, _) =>
            {
                hits++;
                if (hits == 1)
                {
                    return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent("""
                        {
                          "name": "documents",
                          "dimension": 3,
                          "metric": "cosine",
                          "host": "documents-abc.svc.us-east-1-aws.pinecone.io"
                        }
                        """, System.Text.Encoding.UTF8, "application/json"),
                    });
                }

                throw new HttpRequestException("data plane down");
            },
        });
        var records = new List<VectorRecord>
        {
            new("id1", new float[] { 1, 2, 3 }, new Dictionary<string, object>()),
        };

        // Act
        var result = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.Network");
    }
}
