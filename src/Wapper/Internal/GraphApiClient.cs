using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wapper.RateLimiting;

namespace Wapper.Internal;

/// <summary>
/// Transport to the Graph API. Resolves credentials, paces the call against every budget it
/// spends, sends it, retries what Meta says is worth retrying, and turns a final failure
/// into a typed exception carrying the parsed error object.
/// </summary>
/// <remarks>
/// The logger is optional so the client can be built by hand — in a test, or outside a host
/// — without wiring up logging. Under a host it is always supplied.
/// </remarks>
internal sealed partial class GraphApiClient(
    HttpClient httpClient,
    IWhatsAppCredentialsProvider credentialsProvider,
    IWhatsAppRateLimiter rateLimiter,
    IOptionsMonitor<WhatsAppOptions> options,
    TimeProvider time,
    ILogger<GraphApiClient>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<GraphApiClient>.Instance;

    /// <summary>Resolves the credentials of a tenant.</summary>
    public ValueTask<WhatsAppCredentials> ResolveCredentialsAsync(
        string tenant,
        CancellationToken cancellationToken) =>
        credentialsProvider.GetCredentialsAsync(tenant, cancellationToken);

    /// <summary>Sends a request, pacing and retrying it, and deserializes the response.</summary>
    /// <remarks>
    /// One span covers the whole thing — the waits and the retries included — because that is
    /// what the caller experiences. The individual attempts show up underneath it when the
    /// host instruments <c>HttpClient</c> as well.
    /// </remarks>
    public async Task<TResponse> SendAsync<TResponse>(
        GraphRequest request,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken)
    {
        using var activity = WhatsAppDiagnostics.StartCall(request);

        try
        {
            var response = await SendWithRetriesAsync(request, responseTypeInfo, activity, cancellationToken)
                .ConfigureAwait(false);

            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (Exception exception)
        {
            WhatsAppDiagnostics.RecordFailure(activity, exception);
            throw;
        }
    }

    private async Task<TResponse> SendWithRetriesAsync<TResponse>(
        GraphRequest request,
        JsonTypeInfo<TResponse> responseTypeInfo,
        Activity? activity,
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

        // How long this call will sit in the limiter. It starts at the configured ceiling and
        // grows to cover a backoff this call imposed on itself; see where it is raised below.
        var maxWait = limits.MaxWait;

        for (var attempt = 0; ; attempt++)
        {
            await rateLimiter.WaitAsync(budgets, maxWait, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                return await SendOnceAsync(request, tenantOptions, responseTypeInfo, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (WhatsAppApiException exception)
            {
                var retryable = ThrottlePolicy.ShouldRetry(exception.Error, out var budget);
                var backoff = ThrottlePolicy.Backoff(attempt, exception.RetryAfter);

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
                        var scope = ScopeFor(request, budgets, spentOnce);
                        Log.HoldingBudgetBack(_logger, scope.Budget, scope.RedactedKey, backoff.TotalSeconds, exception.Code);

                        await rateLimiter
                            .PenaliseAsync(scope, backoff, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    throw budget is { } exhausted
                        ? new WhatsAppRateLimitedException(
                            ScopeFor(request, budgets, exhausted),
                            backoff,
                            exception)
                        : exception;
                }

                Log.Retrying(_logger, request.Method, request.Path, exception.Code, attempt + 1, backoff.TotalSeconds);
                WhatsAppDiagnostics.RecordRetry(activity, attempt + 1, exception.Code);

                if (budget is { } spent)
                {
                    // Holding the budget back is also the wait: the next pass through
                    // WaitAsync will not come back until the penalty has run out, and every
                    // other call sharing that budget is held with it. Meta counts rejected
                    // calls too, so carrying on regardless would extend the block.
                    var scope = ScopeFor(request, budgets, spent);
                    Log.HoldingBudgetBack(_logger, scope.Budget, scope.RedactedKey, backoff.TotalSeconds, exception.Code);

                    await rateLimiter
                        .PenaliseAsync(scope, backoff, cancellationToken)
                        .ConfigureAwait(false);

                    // MaxWait is what a caller will wait for somebody else's traffic. This
                    // hold is our own backoff, and refusing to sit it out would turn the
                    // retry into the failure: the caller would get a rate-limit exception
                    // about a wait this very call asked for, with the Cloud API's own error
                    // nowhere in it. Meta's 4^X reaches 64 seconds by the fourth retry, which
                    // is longer than any sane MaxWait.
                    maxWait = backoff > limits.MaxWait ? backoff : limits.MaxWait;
                }
                else
                {
                    await Task.Delay(backoff, time, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Sends a request whose whole answer is whether it worked, and throws when the Cloud
    /// API says it did not.
    /// </summary>
    /// <remarks>
    /// These endpoints answer <c>{"success": true}</c>. A body without the field is
    /// tolerated, but an explicit <c>"success": false</c> on a 200 would otherwise complete
    /// as a success — and a subscription that silently never happened is undebuggable.
    /// </remarks>
    public async Task SendAsync(GraphRequest request, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
                request,
                WhatsAppJsonContext.Default.SuccessResponse,
                cancellationToken)
            .ConfigureAwait(false);

        if (response.Success is false)
        {
            throw new WhatsAppException(
                $"The Cloud API answered {request.Method} {request.Path} with " +
                "\"success\": false and no error object, so the call did not take effect " +
                "and there is no code to say why.");
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
        using var activity = WhatsAppDiagnostics.StartCall(request);
        var tenantOptions = options.Get(request.Tenant);

        // The URL came back from the Cloud API, but it reaches this method through a public
        // one that takes a MediaInfo the caller can build. Anything wrong or hostile in it
        // would be handed a working access token.
        GuardFetchUri(tenantOptions, absoluteUri);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, absoluteUri);
        httpRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", request.Credentials.AccessToken);

        using var timeout = new CancellationTokenSource(tenantOptions.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        var response = await SendCoreAsync(
                httpRequest,
                request,
                tenantOptions,
                cancellationToken,
                linked.Token)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }

        try
        {
            var failure = new WhatsAppApiException(
                await ParseErrorAsync(response, linked.Token).ConfigureAwait(false),
                response.StatusCode,
                RetryAfterOf(response));

            WhatsAppDiagnostics.RecordFailure(activity, failure);
            throw failure;
        }
        finally
        {
            response.Dispose();
        }
    }

    /// <summary>
    /// The WhatsApp Business Account id, which every account-level call is addressed to.
    /// </summary>
    /// <remarks>
    /// Optional in configuration, because an application that only sends messages never needs
    /// it. One that manages the account does, so the failure names the setting.
    /// </remarks>
    internal static string RequireBusinessAccount(WhatsAppCredentials credentials) =>
        credentials.WhatsAppBusinessAccountId
        ?? throw new WhatsAppConfigurationException(
            "This call is made against the WhatsApp Business Account, and its id is not " +
            "configured. Set WhatsApp:WhatsAppBusinessAccountId, or return it from your " +
            $"{nameof(IWhatsAppCredentialsProvider)}.");

    /// <summary>
    /// The Meta app id, which the resumable upload endpoint is addressed to.
    /// </summary>
    /// <remarks>
    /// Optional in configuration for the same reason as the business account id: only one
    /// endpoint needs it, and most applications never call it.
    /// </remarks>
    internal static string RequireApp(WhatsAppCredentials credentials) =>
        credentials.AppId
        ?? throw new WhatsAppConfigurationException(
            "Uploading a file to Meta is addressed to your app rather than to a WhatsApp " +
            "object, and the app id is not configured. Set WhatsApp:AppId, or return it from " +
            $"your {nameof(IWhatsAppCredentialsProvider)}.");

    /// <summary>
    /// An identifier a caller handed in, made safe to put in a request path.
    /// </summary>
    /// <remarks>
    /// Graph ids are digits, so a legitimate one comes out unchanged. Anything else is refused
    /// before it reaches the wire: a media id of <c>../123/message_templates?name=x</c>, say,
    /// would otherwise turn a media delete into a template delete — under the caller's token,
    /// against a resource the caller never named. Escaping alone is not enough, because
    /// <see cref="Uri"/> folds a bare <c>..</c> whether it is escaped or not.
    /// </remarks>
    internal static string PathSegment(
        string id,
        [CallerArgumentExpression(nameof(id))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id, paramName);

        if (id is "." or ".." || id.AsSpan().IndexOfAny("/\\?#") >= 0)
        {
            throw new ArgumentException(
                $"'{id}' is not something this client will put in a request path. A Graph " +
                "identifier carries no slashes, dots or query characters.",
                paramName);
        }

        return Uri.EscapeDataString(id);
    }

    internal static Uri BuildUri(WhatsAppOptions options, string path)
    {
        // A leading slash on the relative part would drop the base path, and a base address
        // without a trailing slash would drop its own last segment. Normalise both.
        var root = options.BaseAddress.AbsoluteUri.EndsWith('/')
            ? options.BaseAddress
            : new Uri(options.BaseAddress.AbsoluteUri + "/");

        // Resolved from one string rather than against the versioned root, because a resumable
        // upload session id begins `upload:` — which Uri reads as a scheme when it is the
        // whole of the relative part, and as an ordinary segment when it is not.
        var uri = new Uri(root, $"{options.GraphApiVersion}/{path.TrimStart('/')}");
        var versioned = new Uri(root, $"{options.GraphApiVersion}/");

        // A path that climbs out from under the API version is either a bug or an id that
        // was never checked. `..` folds whether it is escaped or not, so comparing the built
        // address against the root it was meant to sit under is the only way to catch it.
        if (!versioned.IsBaseOf(uri))
        {
            throw new WhatsAppException(
                $"'{path}' does not stay under {versioned}, so it would address something " +
                "other than the endpoint it names.");
        }

        return uri;
    }

    /// <summary>
    /// Refuses to present the access token to a host that is not Meta's.
    /// </summary>
    /// <remarks>
    /// A media URL is not a Graph API address: Meta returns a host of its own choosing, and
    /// the download only works with the bearer token attached. That makes the URL a place a
    /// token can be sent, so where it points has to be checked rather than trusted — a stored
    /// or replayed <c>MediaInfo</c> pointing somewhere else would otherwise collect a working
    /// token for the whole account.
    /// </remarks>
    internal static void GuardFetchUri(WhatsAppOptions options, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new WhatsAppException(
                $"A media download has to be an absolute https URL, and '{uri}' is not. The " +
                "access token travels with the request, and anything else would put it on the " +
                "wire in the clear.");
        }

        if (IsAllowedFetchHost(options, uri.Host))
        {
            return;
        }

        throw new WhatsAppException(
            $"'{uri.Host}' is not a host this client will present the access token to. Media " +
            "downloads come back on Meta's own hosts; if Meta has started using another one, " +
            $"add it to {nameof(WhatsAppOptions)}.{nameof(WhatsAppOptions.MediaDownloadHosts)}.");
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
            // rejection can hold the whole application back.
            RateLimitRequest.Unpaced(ApplicationScope(request)),
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
                    BusinessAccountScope(request),
                    limits.BusinessAccountRequestsPerHour / 3600d,
                    limits.BusinessAccountRequestsPerHour));
                break;

            case GraphCallKind.Other:
            default:
                break;
        }

        return budgets;
    }

    /// <summary>
    /// The platform-wide budget this call spends.
    /// </summary>
    /// <remarks>
    /// Keyed by Meta app id, because that is what Meta counts. A host serving many tenants
    /// through one app has to hold all of them back when the app is blocked: holding only the
    /// tenant that discovered it would leave the rest hammering a block that gets longer the
    /// more it is hammered. Falls back to the tenant name when no app id is configured, which
    /// is the best a single-tenant application can do and costs it nothing.
    /// </remarks>
    private static RateLimitScope ApplicationScope(GraphRequest request) =>
        RateLimitScope.ApplicationRequests(request.Credentials.AppId ?? request.Tenant);

    /// <remarks>
    /// Falls back to the phone number so a tenant that never configured the account id still
    /// counts its management calls against something stable.
    /// </remarks>
    private static RateLimitScope BusinessAccountScope(GraphRequest request) =>
        RateLimitScope.BusinessAccountRequests(
            request.Credentials.WhatsAppBusinessAccountId ?? request.Credentials.PhoneNumberId);

    private static bool IsAllowedFetchHost(WhatsAppOptions options, string host)
    {
        // Whatever the base address points at is by definition somewhere this client already
        // sends the token, which is what makes a proxy or a test server work.
        if (options.BaseAddress.IsAbsoluteUri
            && string.Equals(host, options.BaseAddress.Host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var allowed in options.MediaDownloadHosts)
        {
            if (string.IsNullOrEmpty(allowed))
            {
                continue;
            }

            if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // A suffix match, and only on a label boundary: "evilfbcdn.net" must not pass
            // for "fbcdn.net".
            if (host.Length > allowed.Length
                && host[host.Length - allowed.Length - 1] == '.'
                && host.EndsWith(allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static RateLimitScope ScopeFor(
        GraphRequest request,
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
        // a call we classified as something else. Key it the way the call would have been
        // keyed had it been classified that way, so the hold lands on the budget that is
        // actually spent rather than on a scope nothing else will ever look up.
        return budget switch
        {
            RateLimitBudget.ApplicationRequests => ApplicationScope(request),
            RateLimitBudget.BusinessAccountRequests => BusinessAccountScope(request),
            RateLimitBudget.PhoneNumberThroughput =>
                RateLimitScope.PhoneNumberThroughput(request.Credentials.PhoneNumberId),
            RateLimitBudget.RecipientPair when request.Recipient is { } recipient =>
                RateLimitScope.RecipientPair(request.Credentials.PhoneNumberId, recipient),
            _ => new RateLimitScope(budget, request.Credentials.PhoneNumberId),
        };
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
        request.Configure?.Invoke(httpRequest);

        // The shared HttpClient has no timeout of its own so that each tenant can set one.
        using var timeout = new CancellationTokenSource(tenantOptions.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        using var response = await SendCoreAsync(
                httpRequest,
                request,
                tenantOptions,
                cancellationToken,
                linked.Token)
            .ConfigureAwait(false);

        await ApplyUsageHeadersAsync(request, tenantOptions, response, linked.Token)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new WhatsAppApiException(
                await ParseErrorAsync(response, linked.Token).ConfigureAwait(false),
                response.StatusCode,
                RetryAfterOf(response));
        }

        var result = await response.Content
            .ReadFromJsonAsync(responseTypeInfo, linked.Token)
            .ConfigureAwait(false);

        return result ?? throw new WhatsAppException(
            $"The Cloud API returned an empty body for {request.Method} {request.Path}, which is " +
            "never valid for this endpoint.");
    }

    /// <summary>
    /// Sends the request, and turns the two ways it can fail without ever reaching Meta into
    /// something a caller can tell apart.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A per-tenant timeout is enforced with a token, so it surfaces as a cancellation that
    /// looks exactly like the caller's own — except that the caller's token is not the one
    /// that fired. Reporting it as a cancellation would have a request handler log a
    /// disconnect for what is really an unreachable Cloud API.
    /// </para>
    /// <para>
    /// Neither failure is retried. Nothing came back, so there is no saying whether the
    /// message was accepted before the connection died, and Meta offers no idempotency key to
    /// settle it — sending again could deliver the message twice.
    /// </para>
    /// </remarks>
    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpRequestMessage httpRequest,
        GraphRequest request,
        WhatsAppOptions tenantOptions,
        CancellationToken callerToken,
        CancellationToken effectiveToken)
    {
        try
        {
            return await httpClient
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, effectiveToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!callerToken.IsCancellationRequested)
        {
            throw new WhatsAppException(
                $"{request.Method} {request.Path} did not complete within the configured timeout " +
                $"of {tenantOptions.Timeout.TotalSeconds:0.##}s.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new WhatsAppException(
                $"{request.Method} {request.Path} could not be sent: {exception.Message}",
                exception);
        }
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
            var scope = ApplicationScope(request);
            var penalty = PenaltyFor(appUsage);
            Log.UsageNearLimit(_logger, GraphUsageHeaders.AppUsageHeader, appUsage.HighestPercent, scope.Budget, penalty.TotalSeconds);

            await rateLimiter.PenaliseAsync(scope, penalty, cancellationToken).ConfigureAwait(false);
        }

        var businessUsage = GraphUsageHeaders.ReadBusinessUseCaseUsage(response);
        if (businessUsage.IsOverThreshold(limits.UsagePercentThreshold))
        {
            var scope = BusinessAccountScope(request);
            var penalty = PenaltyFor(businessUsage);
            Log.UsageNearLimit(_logger, GraphUsageHeaders.BusinessUseCaseUsageHeader, businessUsage.HighestPercent, scope.Budget, penalty.TotalSeconds);

            await rateLimiter.PenaliseAsync(scope, penalty, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// The <c>Retry-After</c> of a response, when there is one.
    /// </summary>
    /// <remarks>
    /// The Cloud API does not document this header and normally does not send it, so this is
    /// read opportunistically and never depended on: something in front of Meta may add one.
    /// </remarks>
    private TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is not { } retryAfter)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta > TimeSpan.Zero ? delta : null;
        }

        if (retryAfter.Date is { } date)
        {
            var wait = date - time.GetUtcNow();
            return wait > TimeSpan.Zero ? wait : null;
        }

        return null;
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
                return error.ToError();
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

    /// <summary>
    /// What the client says about pacing itself. Nothing here carries a customer's number in
    /// full: a pair scope is logged through its redacted key.
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "The Cloud API rejected {Method} {Path} with error {Code}; retry {Attempt} in {DelaySeconds}s.")]
        public static partial void Retrying(
            ILogger logger,
            HttpMethod method,
            string path,
            int code,
            int attempt,
            double delaySeconds);

        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Warning,
            Message = "Holding the {Budget} budget for '{Scope}' back for {HoldSeconds}s after error {Code}.")]
        public static partial void HoldingBudgetBack(
            ILogger logger,
            RateLimitBudget budget,
            string scope,
            double holdSeconds,
            int code);

        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Warning,
            Message = "{Header} reports {Percent}% of the allowance spent; holding the {Budget} budget back for {HoldSeconds}s.")]
        public static partial void UsageNearLimit(
            ILogger logger,
            string header,
            int percent,
            RateLimitBudget budget,
            double holdSeconds);
    }
}
