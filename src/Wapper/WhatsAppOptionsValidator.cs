using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Wapper.RateLimiting;

namespace Wapper;

/// <summary>
/// Fails startup on configuration that could only fail later, in production, as an
/// unhelpful HTTP error.
/// </summary>
internal sealed partial class WhatsAppOptionsValidator : IValidateOptions<WhatsAppOptions>
{
    public ValidateOptionsResult Validate(string? name, WhatsAppOptions options)
    {
        var failures = new List<string>();
        var tenant = string.IsNullOrEmpty(name) ? "the default tenant" : $"tenant '{name}'";

        if (!GraphApiVersionPattern().IsMatch(options.GraphApiVersion))
        {
            failures.Add(
                $"GraphApiVersion of {tenant} is '{options.GraphApiVersion}'; expected a Graph API " +
                "version such as 'v26.0'.");
        }

        if (!options.BaseAddress.IsAbsoluteUri)
        {
            failures.Add($"BaseAddress of {tenant} must be an absolute URI.");
        }
        else if (options.BaseAddress.Scheme != Uri.UriSchemeHttps && !options.BaseAddress.IsLoopback)
        {
            // The access token is a bearer token: it is worth exactly as much to whoever
            // reads it off the wire. Loopback is exempt so a test server or a local proxy
            // does not have to hold a certificate.
            failures.Add(
                $"BaseAddress of {tenant} is '{options.BaseAddress}', which is not https. The " +
                "access token is sent with every request and must not travel in the clear.");
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            failures.Add($"Timeout of {tenant} must be greater than zero.");
        }

        ValidateRateLimits(tenant, options.RateLimits, failures);

        // AccessToken and PhoneNumberId are deliberately not required here. A multi-tenant
        // host supplies them from its own store through IWhatsAppCredentialsProvider, and
        // demanding them in configuration would make that arrangement impossible.
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <remarks>
    /// Every one of these is a value the limiter divides by or paces against, and a zero
    /// turns pacing into either an infinite wait or no limit at all — both of which only
    /// show up in production, as messages that never send or as a block from Meta.
    /// </remarks>
    private static void ValidateRateLimits(
        string tenant,
        WhatsAppRateLimitOptions limits,
        List<string> failures)
    {
        if (limits.MessagesPerSecond <= 0)
        {
            failures.Add(
                $"RateLimits.MessagesPerSecond of {tenant} is {limits.MessagesPerSecond}. It is " +
                "the throughput of the business phone number, so it has to be at least one — " +
                "Meta's own floor is 80.");
        }

        if (limits.PairInterval <= TimeSpan.Zero)
        {
            failures.Add(
                $"RateLimits.PairInterval of {tenant} must be greater than zero. Meta allows one " +
                "message every six seconds to the same recipient.");
        }

        if (limits.PairBurst <= 0)
        {
            failures.Add(
                $"RateLimits.PairBurst of {tenant} is {limits.PairBurst}; it has to be at least " +
                "one, or no message could ever be sent to a recipient.");
        }

        if (limits.BusinessAccountRequestsPerHour <= 0)
        {
            failures.Add(
                $"RateLimits.BusinessAccountRequestsPerHour of {tenant} is " +
                $"{limits.BusinessAccountRequestsPerHour}; it has to be at least one, or no " +
                "management call could ever be made.");
        }

        if (limits.MaxWait < TimeSpan.Zero)
        {
            failures.Add($"RateLimits.MaxWait of {tenant} cannot be negative.");
        }

        if (limits.MaxRetries < 0)
        {
            failures.Add($"RateLimits.MaxRetries of {tenant} cannot be negative.");
        }

        if (limits.UsagePercentThreshold is <= 0 or > 100)
        {
            failures.Add(
                $"RateLimits.UsagePercentThreshold of {tenant} is {limits.UsagePercentThreshold}; " +
                "it is a percentage of Meta's allowance, so it belongs between 1 and 100. Meta " +
                "throttles at 100.");
        }
    }

    [GeneratedRegex(@"^v\d+\.\d+$")]
    private static partial Regex GraphApiVersionPattern();
}
