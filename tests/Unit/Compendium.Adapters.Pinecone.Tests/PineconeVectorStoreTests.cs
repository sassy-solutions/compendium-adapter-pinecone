// -----------------------------------------------------------------------
// <copyright file="PineconeVectorStoreTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Text.Json;
using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Pinecone.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace Compendium.Adapters.Pinecone.Tests;

/// <summary>
/// Behavioural coverage for <see cref="PineconeVectorStore"/> driven by
/// <see cref="MockHttpMessageHandler"/>. No real Pinecone is ever touched.
/// Asserts the wire shape we send (across both the control plane and the
/// per-index data plane) and how we map canned responses back to <c>Result</c> values.
/// </summary>
public class PineconeVectorStoreTests
{
    private const string ControlUrl = "https://api.pinecone.io";
    private const string DataHost = "documents-abc123.svc.us-east-1-aws.pinecone.io";
    private const string DataUrl = "https://documents-abc123.svc.us-east-1-aws.pinecone.io";

    private static (PineconeVectorStore Store, MockHttpMessageHandler Mock) CreateStore(
        PineconeOptions? options = null,
        ILogger<PineconeVectorStore>? logger = null)
    {
        var mock = new MockHttpMessageHandler();
        var http = mock.ToHttpClient();
        var opts = Microsoft.Extensions.Options.Options.Create(options ?? new PineconeOptions
        {
            ApiKey = "test-key",
            ControlPlaneBaseUrl = ControlUrl,
        });
        var store = new PineconeVectorStore(http, opts, logger ?? NullLogger<PineconeVectorStore>.Instance);
        return (store, mock);
    }

    /// <summary>Default describe-index response used to seed the data-plane host cache.</summary>
    private static string DescribeIndexBody(int dim = 3, string metric = "cosine") => $$"""
    {
      "name": "documents",
      "dimension": {{dim}},
      "metric": "{{metric}}",
      "host": "{{DataHost}}",
      "spec": { "serverless": { "cloud": "aws", "region": "us-east-1" } },
      "status": { "ready": true, "state": "Ready" }
    }
    """;

    // ─── Constructor ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullHttpClient_Throws()
    {
        // Arrange / Act
        var act = () => new PineconeVectorStore(
            null!,
            Microsoft.Extensions.Options.Options.Create(new PineconeOptions { ApiKey = "k" }),
            NullLogger<PineconeVectorStore>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        // Arrange / Act
        var act = () => new PineconeVectorStore(new HttpClient(), null!, NullLogger<PineconeVectorStore>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        // Arrange / Act
        var act = () => new PineconeVectorStore(
            new HttpClient(),
            Microsoft.Extensions.Options.Options.Create(new PineconeOptions { ApiKey = "k" }),
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_EmptyApiKey_Throws()
    {
        // Arrange
        var opts = Microsoft.Extensions.Options.Options.Create(new PineconeOptions { ApiKey = string.Empty });

        // Act
        var act = () => new PineconeVectorStore(new HttpClient(), opts, NullLogger<PineconeVectorStore>.Instance);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_EmptyControlPlaneBaseUrl_Throws()
    {
        // Arrange
        var opts = Microsoft.Extensions.Options.Options.Create(new PineconeOptions
        {
            ApiKey = "k",
            ControlPlaneBaseUrl = string.Empty,
        });

        // Act
        var act = () => new PineconeVectorStore(new HttpClient(), opts, NullLogger<PineconeVectorStore>.Instance);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullOptionsValue_Throws()
    {
        // Arrange
        var opts = Substitute.For<IOptions<PineconeOptions>>();
        opts.Value.Returns((PineconeOptions)null!);

        // Act
        var act = () => new PineconeVectorStore(new HttpClient(), opts, NullLogger<PineconeVectorStore>.Instance);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RegistersApiKeyHeader()
    {
        // Arrange
        var http = new HttpClient();
        var opts = Microsoft.Extensions.Options.Options.Create(new PineconeOptions { ApiKey = "secret-key" });

        // Act
        _ = new PineconeVectorStore(http, opts, NullLogger<PineconeVectorStore>.Instance);

        // Assert
        http.DefaultRequestHeaders.GetValues("Api-Key").Should().ContainSingle().Which.Should().Be("secret-key");
    }

    // ─── EnsureCollectionAsync ────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EnsureCollectionAsync_BadCollection_ReturnsValidation(string? collection)
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.EnsureCollectionAsync(collection!, 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.InvalidIndex");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task EnsureCollectionAsync_NonPositiveDimension_ReturnsValidation(int dim)
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.EnsureCollectionAsync("documents", dim, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.InvalidDimension");
    }

    [Fact]
    public async Task EnsureCollectionAsync_InvalidDistanceMetricEnumValue_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, (DistanceMetric)999, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.InvalidDistanceMetric");
    }

    [Fact]
    public async Task EnsureCollectionAsync_BadIndexCharacters_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act — Pinecone index names cannot contain underscores or uppercase.
        var result = await store.EnsureCollectionAsync("Bad_Name", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.InvalidIndex");
    }

    [Fact]
    public async Task EnsureCollectionAsync_GetReturns404_CallsCreate()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");
        string? capturedBody = null;
        mock.When(HttpMethod.Post, $"{ControlUrl}/indexes")
            .With(req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", DescribeIndexBody());

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("name").GetString().Should().Be("documents");
        doc.RootElement.GetProperty("dimension").GetInt32().Should().Be(3);
        doc.RootElement.GetProperty("metric").GetString().Should().Be("cosine");
        doc.RootElement.GetProperty("spec").GetProperty("serverless").GetProperty("cloud").GetString().Should().Be("aws");
        doc.RootElement.GetProperty("spec").GetProperty("serverless").GetProperty("region").GetString().Should().Be("us-east-1");
    }

    [Theory]
    [InlineData(DistanceMetric.L2, "euclidean")]
    [InlineData(DistanceMetric.InnerProduct, "dotproduct")]
    [InlineData(DistanceMetric.Cosine, "cosine")]
    public async Task EnsureCollectionAsync_MapsDistanceMetric(DistanceMetric metric, string expectedLabel)
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");
        string? capturedBody = null;
        mock.When(HttpMethod.Post, $"{ControlUrl}/indexes")
            .With(req => { capturedBody = req.Content!.ReadAsStringAsync().Result; return true; })
            .Respond("application/json", DescribeIndexBody(metric: expectedLabel));

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, metric, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedBody.Should().Contain($"\"metric\":\"{expectedLabel}\"");
    }

    [Fact]
    public async Task EnsureCollectionAsync_AlreadyExistsCompatible_SkipsCreate()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());
        var postHits = 0;
        mock.When(HttpMethod.Post, $"{ControlUrl}/indexes")
            .Respond(_ => { postHits++; return new HttpResponseMessage(HttpStatusCode.OK); });

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        postHits.Should().Be(0);
    }

    [Fact]
    public async Task EnsureCollectionAsync_ExistsWithDifferentDimension_ReturnsDimensionMismatch()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody(dim: 5));

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VectorStore.DimensionMismatch");
    }

    [Fact]
    public async Task EnsureCollectionAsync_ExistsWithDifferentMetric_ReturnsConflict()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody(metric: "euclidean"));

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.MetricMismatch");
    }

    [Fact]
    public async Task EnsureCollectionAsync_CreateReturns409_TreatsAsSuccess()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");
        mock.When(HttpMethod.Post, $"{ControlUrl}/indexes")
            .Respond(HttpStatusCode.Conflict, "application/json", """{"code":"ALREADY_EXISTS","message":"index already exists"}""");

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureCollectionAsync_GetFails500_PropagatesError()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond(HttpStatusCode.InternalServerError, "text/plain", "boom");

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.ServerError");
    }

    [Fact]
    public async Task EnsureCollectionAsync_CreateFails500_PropagatesError()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");
        mock.When(HttpMethod.Post, $"{ControlUrl}/indexes")
            .Respond(HttpStatusCode.InternalServerError, "text/plain", "boom");

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.ServerError");
    }

    [Fact]
    public async Task EnsureCollectionAsync_CancellationPropagates()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = () => store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task EnsureCollectionAsync_AppliesIndexPrefix()
    {
        // Arrange
        var (store, mock) = CreateStore(new PineconeOptions
        {
            ApiKey = "k",
            ControlPlaneBaseUrl = ControlUrl,
            IndexPrefix = "dev-",
        });
        string? capturedBody = null;
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/dev-documents")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");
        mock.When(HttpMethod.Post, $"{ControlUrl}/indexes")
            .With(req => { capturedBody = req.Content!.ReadAsStringAsync().Result; return true; })
            .Respond("application/json", DescribeIndexBody());

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedBody.Should().Contain("\"name\":\"dev-documents\"");
    }

    // ─── UpsertAsync ──────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpsertAsync_BadCollection_ReturnsValidation(string? collection)
    {
        // Arrange
        var (store, _) = CreateStore();
        var records = new List<VectorRecord>
        {
            new("id1", new float[] { 1, 2, 3 }, new Dictionary<string, object>()),
        };

        // Act
        var result = await store.UpsertAsync(collection!, records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.InvalidIndex");
    }

    [Fact]
    public async Task UpsertAsync_BadIndexCharacters_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();
        var records = new List<VectorRecord>
        {
            new("id1", new float[] { 1, 2, 3 }, new Dictionary<string, object>()),
        };

        // Act
        var result = await store.UpsertAsync("BAD_NAME", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.InvalidIndex");
    }

    [Fact]
    public async Task UpsertAsync_NullRecords_Throws()
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        Func<Task> act = () => store.UpsertAsync("documents", null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpsertAsync_EmptyList_ReturnsSuccessWithoutHittingNetwork()
    {
        // Arrange
        var (store, mock) = CreateStore();

        // Act
        var result = await store.UpsertAsync("documents", new List<VectorRecord>(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        mock.GetMatchCount(mock.Fallback).Should().Be(0);
    }

    [Fact]
    public async Task UpsertAsync_BlankId_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();
        var records = new List<VectorRecord>
        {
            new("  ", new float[] { 1, 2, 3 }, new Dictionary<string, object>()),
        };

        // Act
        var result = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.InvalidRecordId");
    }

    [Fact]
    public async Task UpsertAsync_NullRecordEntry_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();
        var records = new List<VectorRecord> { null! };

        // Act
        var result = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.InvalidRecord");
    }

    [Fact]
    public async Task UpsertAsync_InvalidTenant_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();
        var records = new List<VectorRecord>
        {
            new("id1", new float[] { 1, 2, 3 }, new Dictionary<string, object>(), "bad tenant"),
        };

        // Act
        var result = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.InvalidTenantId");
    }

    [Fact]
    public async Task UpsertAsync_NamespaceMode_SendsNamespaceField()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());

        string? capturedBody = null;
        mock.When(HttpMethod.Post, $"{DataUrl}/vectors/upsert")
            .With(req => { capturedBody = req.Content!.ReadAsStringAsync().Result; return true; })
            .Respond("application/json", """{"upsertedCount":1}""");

        var records = new List<VectorRecord>
        {
            new("id1", new float[] { 1, 2, 3 }, new Dictionary<string, object> { ["title"] = "hello" }, "tenant-1"),
        };

        // Act
        var result = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("namespace").GetString().Should().Be("tenant-1");
        var vector = doc.RootElement.GetProperty("vectors")[0];
        vector.GetProperty("id").GetString().Should().Be("id1");
        vector.GetProperty("metadata").GetProperty("title").GetString().Should().Be("hello");
        vector.GetProperty("metadata").TryGetProperty("tenant_id", out _).Should().BeFalse();
    }

    [Fact]
    public async Task UpsertAsync_MetadataMode_InjectsTenantIdIntoMetadata()
    {
        // Arrange
        var (store, mock) = CreateStore(new PineconeOptions
        {
            ApiKey = "k",
            ControlPlaneBaseUrl = ControlUrl,
            TenancyMode = PineconeTenancyMode.Metadata,
        });
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());
        string? capturedBody = null;
        mock.When(HttpMethod.Post, $"{DataUrl}/vectors/upsert")
            .With(req => { capturedBody = req.Content!.ReadAsStringAsync().Result; return true; })
            .Respond("application/json", """{"upsertedCount":1}""");

        var records = new List<VectorRecord>
        {
            new("id1", new float[] { 1, 2, 3 }, new Dictionary<string, object> { ["title"] = "hello" }, "tenant-1"),
        };

        // Act
        var result = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.TryGetProperty("namespace", out _).Should().BeFalse();
        var vector = doc.RootElement.GetProperty("vectors")[0];
        vector.GetProperty("metadata").GetProperty("tenant_id").GetString().Should().Be("tenant-1");
    }

    [Fact]
    public async Task UpsertAsync_MultipleTenants_FansOutNamespaces()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());

        var hits = new List<string>();
        mock.When(HttpMethod.Post, $"{DataUrl}/vectors/upsert")
            .With(req =>
            {
                hits.Add(req.Content!.ReadAsStringAsync().Result);
                return true;
            })
            .Respond("application/json", """{"upsertedCount":1}""");

        var records = new List<VectorRecord>
        {
            new("a", new float[] { 1, 2, 3 }, new Dictionary<string, object>(), "tenant-1"),
            new("b", new float[] { 1, 2, 3 }, new Dictionary<string, object>(), "tenant-2"),
        };

        // Act
        var result = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        hits.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpsertAsync_IndexNotFound_MapsToVectorStoreError()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");

        var records = new List<VectorRecord>
        {
            new("id1", new float[] { 1, 2, 3 }, new Dictionary<string, object>()),
        };

        // Act
        var result = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VectorStore.CollectionNotFound");
    }

    [Fact]
    public async Task UpsertAsync_DataPlane404_MapsToVectorStoreError()
    {
        // Arrange — describe succeeds, but data-plane upsert returns 404
        // (this is the path Pinecone takes when an index is deleted between describe and upsert).
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());
        mock.When(HttpMethod.Post, $"{DataUrl}/vectors/upsert")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");

        var records = new List<VectorRecord>
        {
            new("id1", new float[] { 1, 2, 3 }, new Dictionary<string, object>()),
        };

        // Act
        var result = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VectorStore.CollectionNotFound");
    }

    [Fact]
    public async Task UpsertAsync_CachesDataPlaneHostAcrossCalls()
    {
        // Arrange — first call resolves host, second call must not re-describe.
        var (store, mock) = CreateStore();
        var describeHits = 0;
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond(_ =>
            {
                describeHits++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(DescribeIndexBody(), System.Text.Encoding.UTF8, "application/json"),
                };
            });
        mock.When(HttpMethod.Post, $"{DataUrl}/vectors/upsert")
            .Respond("application/json", """{"upsertedCount":1}""");

        var records = new List<VectorRecord>
        {
            new("id1", new float[] { 1, 2, 3 }, new Dictionary<string, object>()),
        };

        // Act
        await store.UpsertAsync("documents", records, CancellationToken.None);
        await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert — describe should be called only once thanks to host caching.
        describeHits.Should().Be(1);
    }

    [Fact]
    public async Task UpsertAsync_EnsureCollectionPrimesHostCache_NoExtraDescribe()
    {
        // Arrange — after a successful ensure-create the host should be cached
        // so the first upsert skips the describe round-trip.
        var (store, mock) = CreateStore();
        var describeHits = 0;
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond(_ =>
            {
                describeHits++;
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
                };
            });
        mock.When(HttpMethod.Post, $"{ControlUrl}/indexes")
            .Respond("application/json", DescribeIndexBody());
        mock.When(HttpMethod.Post, $"{DataUrl}/vectors/upsert")
            .Respond("application/json", """{"upsertedCount":1}""");

        var records = new List<VectorRecord>
        {
            new("id1", new float[] { 1, 2, 3 }, new Dictionary<string, object>()),
        };

        // Act
        var ensure = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);
        var upsert = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        ensure.IsSuccess.Should().BeTrue();
        upsert.IsSuccess.Should().BeTrue();
        describeHits.Should().Be(1); // only the initial probe during EnsureCollectionAsync.
    }

    [Fact]
    public async Task UpsertAsync_DescribeMissingHost_ReturnsMissingHost()
    {
        // Arrange — index exists but has no host (still provisioning).
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", """
            {
              "name": "documents",
              "dimension": 3,
              "metric": "cosine",
              "status": { "ready": false, "state": "Initializing" }
            }
            """);

        var records = new List<VectorRecord>
        {
            new("id1", new float[] { 1, 2, 3 }, new Dictionary<string, object>()),
        };

        // Act
        var result = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.MissingHost");
    }

    // ─── DeleteAsync ──────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeleteAsync_BadCollection_ReturnsValidation(string? collection)
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.DeleteAsync(collection!, new List<string> { "id1" }, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.InvalidIndex");
    }

    [Fact]
    public async Task DeleteAsync_BadIndexCharacters_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.DeleteAsync("BAD_NAME", new List<string> { "id1" }, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.InvalidIndex");
    }

    [Fact]
    public async Task DeleteAsync_NullIds_Throws()
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        Func<Task> act = () => store.DeleteAsync("documents", null!, null, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DeleteAsync_EmptyIds_ReturnsSuccessWithoutHittingNetwork()
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.DeleteAsync("documents", new List<string>(), null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_InvalidTenant_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.DeleteAsync(
            "documents",
            new List<string> { "id1" },
            tenantId: "bad tenant",
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.InvalidTenantId");
    }

    [Fact]
    public async Task DeleteAsync_BlankIdEntry_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.DeleteAsync(
            "documents",
            new List<string> { "ok", "  " },
            tenantId: null,
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.InvalidRecordId");
    }

    [Fact]
    public async Task DeleteAsync_NamespaceMode_WithTenant_SendsNamespace()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());
        string? capturedBody = null;
        mock.When(HttpMethod.Post, $"{DataUrl}/vectors/delete")
            .With(req => { capturedBody = req.Content!.ReadAsStringAsync().Result; return true; })
            .Respond("application/json", "{}");

        // Act
        var result = await store.DeleteAsync(
            "documents",
            new List<string> { "a", "b" },
            tenantId: "tenant-1",
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("namespace").GetString().Should().Be("tenant-1");
        doc.RootElement.GetProperty("ids").GetArrayLength().Should().Be(2);
        doc.RootElement.TryGetProperty("filter", out _).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_MetadataMode_WithTenant_SendsTenantFilter()
    {
        // Arrange
        var (store, mock) = CreateStore(new PineconeOptions
        {
            ApiKey = "k",
            ControlPlaneBaseUrl = ControlUrl,
            TenancyMode = PineconeTenancyMode.Metadata,
        });
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());
        string? capturedBody = null;
        mock.When(HttpMethod.Post, $"{DataUrl}/vectors/delete")
            .With(req => { capturedBody = req.Content!.ReadAsStringAsync().Result; return true; })
            .Respond("application/json", "{}");

        // Act
        var result = await store.DeleteAsync(
            "documents",
            new List<string> { "a" },
            tenantId: "tenant-1",
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.TryGetProperty("namespace", out _).Should().BeFalse();
        doc.RootElement.GetProperty("filter").GetProperty("tenant_id").GetProperty("$eq").GetString().Should().Be("tenant-1");
    }

    [Fact]
    public async Task DeleteAsync_NoTenant_SendsIdsOnly()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());
        string? capturedBody = null;
        mock.When(HttpMethod.Post, $"{DataUrl}/vectors/delete")
            .With(req => { capturedBody = req.Content!.ReadAsStringAsync().Result; return true; })
            .Respond("application/json", "{}");

        // Act
        var result = await store.DeleteAsync("documents", new List<string> { "a" }, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.TryGetProperty("namespace", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("filter", out _).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_IndexNotFound_MapsToVectorStoreError()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");

        // Act
        var result = await store.DeleteAsync("documents", new List<string> { "a" }, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VectorStore.CollectionNotFound");
    }

    [Fact]
    public async Task DeleteAsync_DataPlane404_MapsToVectorStoreError()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());
        mock.When(HttpMethod.Post, $"{DataUrl}/vectors/delete")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");

        // Act
        var result = await store.DeleteAsync("documents", new List<string> { "a" }, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VectorStore.CollectionNotFound");
    }

    // ─── SearchAsync ──────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_BadCollection_ReturnsValidation(string? collection)
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.SearchAsync(collection!, new float[] { 1, 2, 3 }, 5, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.InvalidIndex");
    }

    [Fact]
    public async Task SearchAsync_BadIndexCharacters_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.SearchAsync("BAD_NAME", new float[] { 1, 2, 3 }, 5, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.InvalidIndex");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SearchAsync_NonPositiveTopK_ReturnsValidation(int topK)
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, topK, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.InvalidTopK");
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.SearchAsync("documents", ReadOnlyMemory<float>.Empty, 5, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.EmptyQueryVector");
    }

    [Fact]
    public async Task SearchAsync_InvalidFilter_ReturnsInvalidFilter()
    {
        // Arrange
        var (store, _) = CreateStore();
        var filter = VectorFilter.Eq("category", "support").ForTenant("bad tenant");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VectorStore.InvalidFilter");
    }

    [Fact]
    public async Task SearchAsync_NamespaceMode_PutsTenantInNamespace()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());
        string? capturedBody = null;
        mock.When(HttpMethod.Post, $"{DataUrl}/query")
            .With(req => { capturedBody = req.Content!.ReadAsStringAsync().Result; return true; })
            .Respond("application/json", """
            {
              "matches": [
                { "id": "a", "score": 0.9, "metadata": { "title": "alpha" } },
                { "id": "b", "score": 0.7, "metadata": { "title": "beta" } }
              ],
              "namespace": "tenant-1"
            }
            """);
        var filter = VectorFilter.Eq("category", "support").ForTenant("tenant-1");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 0.9f, 0.1f, 0f }, topK: 2, filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value![0].Id.Should().Be("a");
        result.Value[0].Score.Should().BeApproximately(0.9f, 1e-6f);
        result.Value[0].TenantId.Should().Be("tenant-1");
        result.Value[0].Metadata.Should().ContainKey("title");
        result.Value[0].Metadata.Should().NotContainKey("tenant_id");

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("namespace").GetString().Should().Be("tenant-1");
        doc.RootElement.GetProperty("topK").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("includeMetadata").GetBoolean().Should().BeTrue();
        // In namespace mode the tenant clause does NOT appear in the metadata filter.
        var filterJson = doc.RootElement.GetProperty("filter").GetRawText();
        filterJson.Should().NotContain("tenant_id");
    }

    [Fact]
    public async Task SearchAsync_MetadataMode_PutsTenantInFilterTopLevelAnd()
    {
        // Arrange
        var (store, mock) = CreateStore(new PineconeOptions
        {
            ApiKey = "k",
            ControlPlaneBaseUrl = ControlUrl,
            TenancyMode = PineconeTenancyMode.Metadata,
        });
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());
        string? capturedBody = null;
        mock.When(HttpMethod.Post, $"{DataUrl}/query")
            .With(req => { capturedBody = req.Content!.ReadAsStringAsync().Result; return true; })
            .Respond("application/json", """
            {
              "matches": [
                { "id": "a", "score": 0.9, "metadata": { "title": "alpha", "tenant_id": "tenant-1" } }
              ]
            }
            """);
        var filter = VectorFilter.Eq("category", "support").ForTenant("tenant-1");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value![0].TenantId.Should().Be("tenant-1");
        result.Value[0].Metadata.Should().NotContainKey("tenant_id");

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.TryGetProperty("namespace", out _).Should().BeFalse();
        doc.RootElement.GetProperty("filter").GetProperty("$and").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task SearchAsync_NoFilter_SendsNoFilterAndDefaultNamespace()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());
        string? capturedBody = null;
        mock.When(HttpMethod.Post, $"{DataUrl}/query")
            .With(req => { capturedBody = req.Content!.ReadAsStringAsync().Result; return true; })
            .Respond("application/json", """{"matches":[]}""");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, filter: null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.TryGetProperty("namespace", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("filter", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_IndexNotFound_MapsToCollectionNotFound()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, filter: null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VectorStore.CollectionNotFound");
    }

    [Fact]
    public async Task SearchAsync_DataPlane404_MapsToCollectionNotFound()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());
        mock.When(HttpMethod.Post, $"{DataUrl}/query")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, filter: null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VectorStore.CollectionNotFound");
    }

    [Fact]
    public async Task SearchAsync_429_MapsToThrottled()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());
        mock.When(HttpMethod.Post, $"{DataUrl}/query")
            .Respond((HttpStatusCode)429, "application/json", """{"code":"RATE_LIMITED","message":"too many"}""");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, filter: null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.Throttled");
        result.Error.Message.Should().Contain("RATE_LIMITED");
    }

    [Fact]
    public async Task SearchAsync_401_MapsToUnauthorized()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond(HttpStatusCode.Unauthorized, "application/json", """{"code":"UNAUTHORIZED","message":"bad key"}""");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, filter: null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.Unauthorized");
    }

    [Fact]
    public async Task SearchAsync_EmptyMatches_ReturnsEmptyList()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());
        mock.When(HttpMethod.Post, $"{DataUrl}/query")
            .Respond("application/json", """{"matches":[]}""");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, filter: null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_NullMatches_ReturnsEmptyList()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());
        mock.When(HttpMethod.Post, $"{DataUrl}/query")
            .Respond("application/json", "{}");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, filter: null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_500_PropagatesServerError()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());
        mock.When(HttpMethod.Post, $"{DataUrl}/query")
            .Respond(HttpStatusCode.InternalServerError, "text/plain", "boom");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, filter: null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.ServerError");
    }

    [Fact]
    public async Task SearchAsync_GenericHttpError_MapsToHttpError()
    {
        // Arrange — Pinecone returns 400 Bad Request.
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{ControlUrl}/indexes/documents")
            .Respond("application/json", DescribeIndexBody());
        mock.When(HttpMethod.Post, $"{DataUrl}/query")
            .Respond(HttpStatusCode.BadRequest, "application/json", """{"code":"BAD","message":"x"}""");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, filter: null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pinecone.HttpError");
    }
}
