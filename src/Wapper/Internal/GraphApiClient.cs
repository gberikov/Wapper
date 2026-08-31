using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;

namespace Wapper.Internal;

/// <summary>
/// Transport to the Graph API: resolves the tenant's credentials, builds the versioned
/// request path, presents the bearer token, and turns a failed response into a
/// <see cref="WhatsAppApiException"/> carrying the parsed error object.
/// </summary>
internal sealed class GraphApiClient(
    HttpClient httpClient,
    IWhatsAppCredentialsProvider credentialsProvider,
    IOptionsMonitor<WhatsAppOptions> options)
{
    /// <summary>Resolves the credentials of a tenant.</summary>
    public ValueTask<WhatsAppCredentials> ResolveCredentialsAsync(
        string tenant,
        CancellationToken cancellationToken) =>
        credentialsProvider.GetCredentialsAsync(tenant, cancellationToken);

    /// <summary>
    /// Sends a request and deserializes the response.
    /// </summary>
    /// <param name="tenant">Tenant whose options supply the base address and API version.</param>
    /// <param name="credentials">Credentials from <see cref="ResolveCredentialsAsync"/>.</param>
    /// <param name="method">HTTP method.</param>
    /// <param name="path">Path below the API version, for example <c>123456/messages</c>.</param>
    /// <param name="content">Request body, or <see langword="null"/>.</param>
    /// <param name="responseTypeInfo">Source-generated metadata for the response type.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<TResponse> SendAsync<TResponse>(
        string tenant,
        WhatsAppCredentials credentials,
        HttpMethod method,
        string path,
        HttpContent? content,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken)
    {
        var tenantOptions = options.Get(tenant);

        using var request = new HttpRequestMessage(method, BuildUri(tenantOptions, path))
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);

        // The shared HttpClient has no timeout of its own so that each tenant can set one.
        using var timeout = new CancellationTokenSource(tenantOptions.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        using var response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await ReadErrorAsync(response, linked.Token).ConfigureAwait(false);
        }

        var result = await response.Content
            .ReadFromJsonAsync(responseTypeInfo, linked.Token)
            .ConfigureAwait(false);

        return result ?? throw new WhatsAppException(
            $"The Cloud API returned an empty body for {method} {path}, which is never valid " +
            "for this endpoint.");
    }

    internal static Uri BuildUri(WhatsAppOptions options, string path)
    {
        // A leading slash on the relative part would drop the base path, and a base address
        // without a trailing slash would drop its own last segment. Normalise both.
        var root = options.BaseAddress.AbsoluteUri.EndsWith('/')
            ? options.BaseAddress
            : new Uri(options.BaseAddress.AbsoluteUri + "/");

        return new Uri(root, $"{options.GraphApiVersion}/{path.TrimStart('/')}");
    }

    private static async Task<WhatsAppApiException> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var error = await ParseErrorAsync(response, cancellationToken).ConfigureAwait(false);
        return new WhatsAppApiException(error, response.StatusCode);
    }

    private static async Task<WhatsAppError> ParseErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // A failure response is not guaranteed to be the documented envelope: gateways and
        // load balancers return HTML, and a dropped connection returns nothing at all. The
        // status code is a poor error but it is better than an exception raised while
        // reporting an exception.
        try
        {
            var envelope = await response.Content
                .ReadFromJsonAsync(WhatsAppJsonContext.Default.GraphErrorEnvelope, cancellationToken)
                .ConfigureAwait(false);

            if (envelope?.Error is { } error)
            {
                return new WhatsAppError
                {
                    Code = error.Code,
                    Type = error.Type,
                    Message = error.Message,
                    Details = error.ErrorData?.Details,
                    TraceId = error.FbTraceId,
                    IsTransient = error.IsTransient,
                    Subcode = error.ErrorSubcode,
                };
            }
        }
        catch (JsonException)
        {
            // Falls through to the status-only error below.
        }
        catch (NotSupportedException)
        {
            // Content-Type was not JSON. Same treatment.
        }

        return new WhatsAppError
        {
            Code = 0,
            Type = "HttpError",
            Message = $"The Cloud API returned {(int)response.StatusCode} " +
                      $"{response.ReasonPhrase ?? response.StatusCode.ToString()} without a " +
                      "readable error object.",
        };
    }
}
