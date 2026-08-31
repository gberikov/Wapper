using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using Wapper.RateLimiting;

namespace Wapper.Internal;

/// <summary>
/// Transport to the Graph API. Resolves credentials, paces the call against every budget it
/// spends, sends it, retries what Meta says is worth retrying, and turns a final failure
/// into a typed exception carrying the parsed error object.
/// </summary>
internal sealed class GraphApiClient(
    HttpClient httpClient,
    IWhatsAppCredentialsProvider credentialsProvider,
    IWhatsAppRateLimiter rateLimiter,
    IOptionsMonitor<WhatsAppOptions> options,
    TimeProvider time)
{
    /// <summary>Resolves the credentials of a tenant.</summary>
    public ValueTask<WhatsAppCredentials> ResolveCredentialsAsync(
        string tenant,
        CancellationToken cancellationToken) =>
        credentialsProvider.GetCredentialsAsync(tenant, cancellationToken);

    /// <summary>Sends a request, pacing and retrying it, and deserializes the response.</summary>
    public async Task<TResponse> SendAsync<TResponse>(
        GraphRequest request,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken)
    {
        var tenantOptions = options.Get(request.Tenant);
        var limits = tenantOptions.RateLimits;

        if (!limits.Enabled)
        {
            return await SendOnceAsync(request, tenantOptions, responseTypeInfo, cancellationToken)
                .ConfigureAwait(false);
        }

        var budgets = BuildBudgets(request, limits);

        for (var attempt = 0; ; attempt++)
        {
            await rateLimiter.WaitAsync(budgets, limits.MaxWait, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                return await SendOnceAsync(request, tenantOptions, responseTypeInfo, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (WhatsAppApiException exception)
            {
                var retryable = ThrottlePolicy.ShouldRetry(exception.Error, out var budget);
                var backoff = ThrottlePolicy.Backoff(attempt);

                if (!retryable)
                {
                    throw;
                }

                if (attempt >= limits.MaxRetries || !request.Retryable)
                {
                    // Even without a retry, holding the budget back is worth doing: the
                    // rejection says the allowance is spent, and the next call would only
                    // find out the same way.
                    if (budget is { } spentOnce)
                    {
                        await rateLimiter
                            .PenaliseAsync(FindScope(budgets, spentOnce), backoff, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    throw budget is { } exhausted
                        ? new WhatsAppRateLimitedException(
                            FindScope(budgets, exhausted),
                            backoff,
                            exception)
                        : exception;
                }

                if (budget is { } spent)
                {
                    // Holding the budget back is also the wait: the next pass through
                    // WaitAsync will not come back until the penalty has run out, and every
                    // other call sharing that budget is held with it. Meta counts rejected
                    // calls too, so carrying on regardless would extend the block.
                    await rateLimiter
                        .PenaliseAsync(FindScope(budgets, spent), backoff, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(backoff, time, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Fetches an absolute URL that is not part of the Graph API surface, presenting the
    /// tenant's token.
    /// </summary>
    /// <remarks>
    /// Media downloads land on a host of Meta's choosing rather than on graph.facebook.com,
    /// and the URL still needs the bearer token: fetching it without one returns 404. The
    /// response is handed back undisposed, because the caller streams the body out of it.
    /// </remarks>
    public async Task<HttpResponseMessage> FetchAsync(
        GraphRequest request,
        Uri absoluteUri,
        CancellationToken cancellationToken)
    {
        var tenantOptions = options.Get(request.Tenant);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, absoluteUri);
        httpRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", request.Credentials.AccessToken);

        using var timeout = new CancellationTokenSource(tenantOptions.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        var response = await httpClient
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, linked.Token)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        try
        {
            throw new WhatsAppApiException(
                await ParseErrorAsync(response, linked.Token).ConfigureAwait(false),
                response.StatusCode);
        }
        finally
        {
            response.Dispose();
        }
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

    /// <summary>Works out which budgets a call spends, and how large each one is.</summary>
    internal static IReadOnlyList<RateLimitRequest> BuildBudgets(
        GraphRequest request,
        WhatsAppRateLimitOptions limits)
    {
        var budgets = new List<RateLimitRequest>(3)
        {
            // Always present, never paced: the platform allowance is 200 times the number of
            // daily active users, which Meta does not publish. It exists here so that a
            // rejection can hold the whole tenant back.
            RateLimitRequest.Unpaced(RateLimitScope.ApplicationRequests(request.Tenant)),
        };

        switch (request.Kind)
        {
            case GraphCallKind.Message:
                budgets.Add(new RateLimitRequest(
                    RateLimitScope.PhoneNumberThroughput(request.Credentials.PhoneNumberId),
                    limits.MessagesPerSecond,
                    limits.MessagesPerSecond));

                if (!string.IsNullOrEmpty(request.Recipient))
                {
                    budgets.Add(new RateLimitRequest(
                        RateLimitScope.RecipientPair(
                            request.Credentials.PhoneNumberId,
                            request.Recipient),
                        1d / limits.PairInterval.TotalSeconds,
                        limits.PairBurst));
                }

                break;

            case GraphCallKind.Management:
                budgets.Add(new RateLimitRequest(
                    RateLimitScope.BusinessAccountRequests(
                        request.Credentials.WhatsAppBusinessAccountId
                        ?? request.Credentials.PhoneNumberId),
                    limits.BusinessAccountRequestsPerHour / 3600d,
                    limits.BusinessAccountRequestsPerHour));
                break;

            case GraphCallKind.Other:
            default:
                break;
        }

        return budgets;
    }

    private static RateLimitScope FindScope(
        IReadOnlyList<RateLimitRequest> budgets,
        RateLimitBudget budget)
    {
        foreach (var candidate in budgets)
        {
            if (candidate.Scope.Budget == budget)
            {
                return candidate.Scope;
            }
        }

        // The Cloud API named a budget this call was not pacing — a management limit hit by
        // a call we classified as something else. Record it under its own name rather than
        // pretending it was one of ours.
        return new RateLimitScope(budget, "unknown");
    }

    private async Task<TResponse> SendOnceAsync<TResponse>(
        GraphRequest request,
        WhatsAppOptions tenantOptions,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(
            request.Method,
            BuildUri(tenantOptions, request.Path))
        {
            Content = request.Content?.Invoke(),
        };
        httpRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", request.Credentials.AccessToken);

        // The shared HttpClient has no timeout of its own so that each tenant can set one.
        using var timeout = new CancellationTokenSource(tenantOptions.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        using var response = await httpClient
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, linked.Token)
            .ConfigureAwait(false);

        await ApplyUsageHeadersAsync(request, tenantOptions, response, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new WhatsAppApiException(
                await ParseErrorAsync(response, linked.Token).ConfigureAwait(false),
                response.StatusCode);
        }

        var result = await response.Content
            .ReadFromJsonAsync(responseTypeInfo, linked.Token)
            .ConfigureAwait(false);

        return result ?? throw new WhatsAppException(
            $"The Cloud API returned an empty body for {request.Method} {request.Path}, which is " +
            "never valid for this endpoint.");
    }

    /// <summary>
    /// Feeds Meta's usage headers back into the limiter. They arrive on successful responses
    /// too, which is the only chance to slow down before the wall rather than after it.
    /// </summary>
    private async Task ApplyUsageHeadersAsync(
        GraphRequest request,
        WhatsAppOptions tenantOptions,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var limits = tenantOptions.RateLimits;
        if (!limits.Enabled)
        {
            return;
        }

        var appUsage = GraphUsageHeaders.ReadAppUsage(response);
        if (appUsage.IsOverThreshold(limits.UsagePercentThreshold))
        {
            await rateLimiter.PenaliseAsync(
                    RateLimitScope.ApplicationRequests(request.Tenant),
                    PenaltyFor(appUsage),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var businessUsage = GraphUsageHeaders.ReadBusinessUseCaseUsage(response);
        if (businessUsage.IsOverThreshold(limits.UsagePercentThreshold))
        {
            await rateLimiter.PenaliseAsync(
                    RateLimitScope.BusinessAccountRequests(
                        request.Credentials.WhatsAppBusinessAccountId
                        ?? request.Credentials.PhoneNumberId),
                    PenaltyFor(businessUsage),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// How long to hold a budget back on the strength of a usage header. Meta's own estimate
    /// wins when it sends one; otherwise this is a stand-in until the next response says
    /// something better.
    /// </summary>
    private static TimeSpan PenaltyFor(UsageReading reading) =>
        reading.TimeToRegainAccess > TimeSpan.Zero
            ? reading.TimeToRegainAccess
            : TimeSpan.FromSeconds(60);

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
