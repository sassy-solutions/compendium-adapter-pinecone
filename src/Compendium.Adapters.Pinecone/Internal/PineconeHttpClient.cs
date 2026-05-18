// -----------------------------------------------------------------------
// <copyright file="PineconeHttpClient.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Compendium.Adapters.Pinecone.Options;
using Compendium.Core.Results;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.Pinecone.Internal;

/// <summary>
/// Typed HTTP client over the Pinecone REST API. Owns request shaping
/// (<c>Api-Key</c> header, JSON content), serialisation, and translation of
/// non-success status codes into <see cref="Result"/> failures.
/// </summary>
/// <remarks>
/// Pinecone has a two-plane architecture: index lifecycle calls go to a fixed
/// <em>control plane</em> host (e.g. <c>https://api.pinecone.io</c>), while
/// vector read / write calls go to a per-index <em>data plane</em> host
/// returned by the control plane (e.g. <c>my-index-abc123.svc.us-east-1-aws.pinecone.io</c>).
/// This client caches resolved data-plane hosts per index to avoid an extra
/// describe-index call on every read.
/// </remarks>
internal sealed class PineconeHttpClient
{
    private readonly HttpClient _http;
    private readonly PineconeOptions _options;
    private readonly ConcurrentDictionary<string, string> _hostCache = new(StringComparer.Ordinal);

    public PineconeHttpClient(HttpClient http, IOptions<PineconeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        var opts = options.Value ?? throw new ArgumentException("Options.Value is null.", nameof(options));

        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            throw new ArgumentException("PineconeOptions.ApiKey must be configured.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(opts.ControlPlaneBaseUrl))
        {
            throw new ArgumentException("PineconeOptions.ControlPlaneBaseUrl must be configured.", nameof(options));
        }

        _http = http;
        _options = opts;

        if (!_http.DefaultRequestHeaders.Contains("Api-Key"))
        {
            _http.DefaultRequestHeaders.Add("Api-Key", opts.ApiKey);
        }
    }

    /// <summary>Sends a GET against the control plane and returns either the parsed body, or null when the server returned 404.</summary>
    public async Task<Result<TResponse?>> ControlGetOptionalAsync<TResponse>(string path, CancellationToken cancellationToken)
        where TResponse : class
    {
        var url = BuildControlPlaneUrl(path);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.Success<TResponse?>(null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return await MapErrorAsync<TResponse?>(response, "Get", cancellationToken).ConfigureAwait(false);
            }

            var body = await response.Content
                .ReadFromJsonAsync<TResponse>(PineconeJson.Options, cancellationToken)
                .ConfigureAwait(false);
            return Result.Success(body);
        }
        catch (HttpRequestException ex)
        {
            return Error.Failure("Pinecone.Network", $"Pinecone request failed: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Error.Failure("Pinecone.Timeout", $"Pinecone request timed out: {ex.Message}");
        }
    }

    /// <summary>Sends a JSON body against the control plane.</summary>
    public Task<Result<TResponse>> ControlSendJsonAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest body,
        CancellationToken cancellationToken)
        where TResponse : class
        => SendJsonAsync<TRequest, TResponse>(method, BuildControlPlaneUrl(path), body, cancellationToken);

    /// <summary>
    /// Sends a JSON body against the data plane for the supplied index. Resolves and
    /// caches the per-index host on first use.
    /// </summary>
    public async Task<Result<TResponse>> DataSendJsonAsync<TRequest, TResponse>(
        string indexName,
        HttpMethod method,
        string path,
        TRequest body,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var hostResult = await ResolveDataPlaneHostAsync(indexName, cancellationToken).ConfigureAwait(false);
        if (hostResult.IsFailure)
        {
            return Result.Failure<TResponse>(hostResult.Error);
        }

        var url = $"https://{hostResult.Value}{path}";
        return await SendJsonAsync<TRequest, TResponse>(method, url, body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the data-plane host for <paramref name="indexName"/>, caching the
    /// result so subsequent calls are free.
    /// </summary>
    public async Task<Result<string>> ResolveDataPlaneHostAsync(string indexName, CancellationToken cancellationToken)
    {
        if (_hostCache.TryGetValue(indexName, out var cached))
        {
            return Result.Success(cached);
        }

        var description = await ControlGetOptionalAsync<IndexDescription>(
            $"/indexes/{Uri.EscapeDataString(indexName)}",
            cancellationToken).ConfigureAwait(false);

        if (description.IsFailure)
        {
            return Result.Failure<string>(description.Error);
        }

        if (description.Value is null)
        {
            return Error.NotFound("Pinecone.IndexNotFound", $"Pinecone index '{indexName}' does not exist.");
        }

        if (string.IsNullOrEmpty(description.Value.Host))
        {
            return Error.Failure(
                "Pinecone.MissingHost",
                $"Pinecone index '{indexName}' description did not include a host. The index may still be initialising.");
        }

        _hostCache[indexName] = description.Value.Host;
        return Result.Success(description.Value.Host);
    }

    /// <summary>Manually pre-seeds the host cache (used after a successful create-index that returned a host).</summary>
    public void CacheHost(string indexName, string host)
    {
        if (!string.IsNullOrEmpty(host))
        {
            _hostCache[indexName] = host;
        }
    }

    private async Task<Result<TResponse>> SendJsonAsync<TRequest, TResponse>(
        HttpMethod method,
        string url,
        TRequest body,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body, mediaType: new MediaTypeHeaderValue("application/json"), options: PineconeJson.Options),
        };

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return await MapErrorAsync<TResponse>(response, method.Method, cancellationToken).ConfigureAwait(false);
            }

            // Pinecone returns "{}" on some success responses (e.g. delete). Tolerate empty bodies.
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
            {
                // For a reference-type TResponse the contract is "non-null on success",
                // but System.Text.Json yields null for "". Activator.CreateInstance keeps
                // the contract honest for our empty-envelope DTOs (DeleteResponse, etc.).
                return Result.Success((TResponse)Activator.CreateInstance(typeof(TResponse))!);
            }

            var parsed = System.Text.Json.JsonSerializer.Deserialize<TResponse>(content, PineconeJson.Options);
            return Result.Success(parsed!);
        }
        catch (HttpRequestException ex)
        {
            return Error.Failure("Pinecone.Network", $"Pinecone request failed: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Error.Failure("Pinecone.Timeout", $"Pinecone request timed out: {ex.Message}");
        }
    }

    private string BuildControlPlaneUrl(string path)
    {
        var baseUrl = _options.ControlPlaneBaseUrl.TrimEnd('/');
        return baseUrl + (path.StartsWith('/') ? path : "/" + path);
    }

    private static async Task<Result<T>> MapErrorAsync<T>(
        HttpResponseMessage response,
        string verb,
        CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;
        string? remote = null;
        string? remoteCode = null;
        string? remoteMessage = null;

        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            remote = raw.Length > 512 ? raw[..512] : raw;

            // Pinecone returns `{ "code": "...", "message": "..." }` on most error responses.
            // Tolerate plain bodies and proxy errors.
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    var envelope = System.Text.Json.JsonSerializer.Deserialize<PineconeError>(raw, PineconeJson.Options);
                    remoteCode = envelope?.Code;
                    remoteMessage = envelope?.Message;
                }
                catch (System.Text.Json.JsonException)
                {
                    // not a JSON envelope — leave remoteCode/Message null.
                }
            }
        }
        catch
        {
            // ignore — best-effort body capture
        }

        var humanRemote = remoteMessage ?? remote;
        var message = string.IsNullOrEmpty(humanRemote)
            ? $"Pinecone {verb} returned HTTP {statusCode}."
            : $"Pinecone {verb} returned HTTP {statusCode}: {humanRemote}";

        var code = statusCode switch
        {
            401 or 403 => "Pinecone.Unauthorized",
            404 => "Pinecone.NotFound",
            408 => "Pinecone.Timeout",
            409 => "Pinecone.Conflict",
            429 => "Pinecone.Throttled",
            >= 500 => "Pinecone.ServerError",
            _ => "Pinecone.HttpError",
        };

        // Preserve the remote code in the message for easier debugging without
        // leaking it through Error.Code (which is the adapter-stable identifier).
        if (!string.IsNullOrEmpty(remoteCode))
        {
            message = $"[{remoteCode}] {message}";
        }

        return Error.Failure(code, message);
    }
}
