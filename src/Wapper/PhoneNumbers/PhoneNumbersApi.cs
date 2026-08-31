using System.Net.Http.Json;
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
                    Content = () => JsonContent.Create(
                        payload,
                        WhatsAppJsonContext.Default.TwoStepPinPayload),
                },
                WhatsAppJsonContext.Default.SuccessResponse,
                cancellationToken)
            .ConfigureAwait(false);
    }

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
}
