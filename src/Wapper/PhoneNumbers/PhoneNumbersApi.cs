using System.Runtime.CompilerServices;
using Wapper.Internal;

namespace Wapper.PhoneNumbers;

/// <summary>Reading one tenant's business phone numbers.</summary>
internal sealed class PhoneNumbersApi(GraphApiClient client, string tenant) : IPhoneNumbersApi
{
    /// <summary>
    /// The fields to ask for.
    /// </summary>
    /// <remarks>
    /// Graph returns a handful of fields by default — the name, the number and its quality —
    /// and leaves out the ones worth reading it for. <c>status</c> and <c>throughput</c> only
    /// arrive if they are asked for by name.
    /// </remarks>
    private const string Fields =
        "id,display_phone_number,verified_name,status,quality_rating," +
        "code_verification_status,name_status,new_name_status,throughput," +
        "messaging_limit_tier,platform_type,account_mode," +
        "is_official_business_account,is_pin_enabled,last_onboarded_time";

    /// <summary>Meta requires exactly this many digits.</summary>
    private const int PinLength = 6;

    public async IAsyncEnumerable<PhoneNumber> ListAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);
        var accountId = GraphApiClient.RequireBusinessAccount(credentials);
        string? after = null;

        do
        {
            var page = await client.SendAsync(
                    new GraphRequest
                    {
                        Tenant = tenant,
                        Credentials = credentials,
                        Method = HttpMethod.Get,
                        Path = $"{accountId}/phone_numbers?fields={Fields}" +
                               (after is null ? string.Empty : $"&after={Uri.EscapeDataString(after)}"),
                        Kind = GraphCallKind.Management,
                    },
                    WhatsAppJsonContext.Default.PhoneNumberListResponse,
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (var item in page.Data ?? [])
            {
                yield return item.ToPhoneNumber();
            }

            after = page.Paging?.NextCursor;
        }
        while (!string.IsNullOrEmpty(after));
    }

    public async Task<PhoneNumber> GetAsync(
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default)
    {
        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        var payload = await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Get,
                    Path = $"{Target(phoneNumberId, credentials)}?fields={Fields}",
                    Kind = GraphCallKind.Management,
                },
                WhatsAppJsonContext.Default.PhoneNumberPayload,
                cancellationToken)
            .ConfigureAwait(false);

        return payload.ToPhoneNumber();
    }

    public async Task SetTwoStepPinAsync(
        string pin,
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default)
    {
        GuardPin(pin);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);
        var payload = new TwoStepPinPayload { Pin = pin };

        await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    Path = Target(phoneNumberId, credentials),
                    Kind = GraphCallKind.Management,
                    Content = GraphContent.Json(
                        payload,
                        WhatsAppJsonContext.Default.TwoStepPinPayload),
                },
                WhatsAppJsonContext.Default.SuccessResponse,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RequestVerificationCodeAsync(
        VerificationCodeMethod method,
        string language = "en_US",
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    // Query string rather than a body: this is how Meta documents the call, and
                    // the code is delivered out of band anyway.
                    Path = $"{Target(phoneNumberId, credentials)}/request_code" +
                           $"?code_method={ToWire(method)}" +
                           $"&language={Uri.EscapeDataString(language)}",
                    Kind = GraphCallKind.Management,
                    // A retry sends a second code and invalidates the first, which strands
                    // whoever is looking at the message that already arrived.
                    Retryable = false,
                },
                WhatsAppJsonContext.Default.SuccessResponse,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task VerifyAsync(
        string code,
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default)
    {
        var digits = GuardCode(code);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    Path = $"{Target(phoneNumberId, credentials)}/verify_code" +
                           $"?code={Uri.EscapeDataString(digits)}",
                    Kind = GraphCallKind.Management,
                },
                WhatsAppJsonContext.Default.SuccessResponse,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RegisterAsync(
        string pin,
        string? dataLocalizationRegion = null,
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default)
    {
        GuardPin(pin);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);
        var payload = new RegisterPayload
        {
            Pin = pin,
            DataLocalizationRegion = GuardRegion(dataLocalizationRegion),
        };

        await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    Path = $"{Target(phoneNumberId, credentials)}/register",
                    Kind = GraphCallKind.Management,
                    Content = GraphContent.Json(
                        payload,
                        WhatsAppJsonContext.Default.RegisterPayload),
                    // Ten attempts per number per 72 hours, counting the failed ones. Spending
                    // one of them on an automatic retry is not worth it.
                    Retryable = false,
                },
                WhatsAppJsonContext.Default.SuccessResponse,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeregisterAsync(
        string? phoneNumberId = null,
        CancellationToken cancellationToken = default)
    {
        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    Path = $"{Target(phoneNumberId, credentials)}/deregister",
                    Kind = GraphCallKind.Management,
                    // Shares the ten-per-72-hours allowance with registration.
                    Retryable = false,
                },
                WhatsAppJsonContext.Default.SuccessResponse,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string ToWire(VerificationCodeMethod method) => method switch
    {
        VerificationCodeMethod.Sms => "SMS",
        VerificationCodeMethod.Voice => "VOICE",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };

    private static string Target(string? phoneNumberId, WhatsAppCredentials credentials) =>
        string.IsNullOrWhiteSpace(phoneNumberId) ? credentials.PhoneNumberId : phoneNumberId;

    private static void GuardPin(string pin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pin);

        // Checked here because a PIN that is short, long or not numeric comes back as a bare
        // code 100, and because a PIN does not belong in an error message anyone might log.
        if (pin.Length != PinLength || !pin.All(char.IsAsciiDigit))
        {
            throw new ArgumentException(
                $"A two-step verification PIN is exactly {PinLength} digits.",
                nameof(pin));
        }
    }

    /// <summary>
    /// Strips the code down to what Meta accepts.
    /// </summary>
    /// <remarks>
    /// The message that carries it writes the code as <c>123-830</c> and the endpoint wants
    /// <c>123830</c>, so a code copied straight out of the message would otherwise be rejected
    /// as wrong rather than as malformed.
    /// </remarks>
    private static string GuardCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var digits = new string(code.Where(static c => c is not ('-' or ' ')).ToArray());

        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
        {
            throw new ArgumentException(
                "A verification code is digits, optionally written with a hyphen.",
                nameof(code));
        }

        return digits;
    }

    private static string? GuardRegion(string? region)
    {
        if (region is null)
        {
            return null;
        }

        // Two letters, because Meta answers "Germany" or "DEU" with the same bare code 100 it
        // gives every other malformed parameter.
        if (region.Length != 2 || !region.All(char.IsAsciiLetter))
        {
            throw new ArgumentException(
                $"A data localization region is a two-letter ISO 3166 country code, and " +
                $"'{region}' is not one.",
                "dataLocalizationRegion");
        }

        return region.ToUpperInvariant();
    }
}
