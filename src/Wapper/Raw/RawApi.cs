using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Wapper.Internal;

namespace Wapper.Raw;

/// <summary>Calls an endpoint this library does not model, for one tenant.</summary>
internal sealed class RawApi(GraphApiClient client, string tenant) : IRawApi
{
    private const string PhoneNumberPlaceholder = "{phone_number_id}";
    private const string BusinessAccountPlaceholder = "{waba_id}";
    private const string AppPlaceholder = "{app_id}";

    public Task<JsonElement> SendAsync(
        RawRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync(request, WhatsAppJsonContext.Default.JsonElement, cancellationToken);

    public async Task<TResponse> SendAsync<TResponse>(
        RawRequest request,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseTypeInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        return await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = request.Method,
                    Path = Expand(request.Path, credentials),
                    Kind = request.Kind switch
                    {
                        RawCallKind.Message => GraphCallKind.Message,
                        RawCallKind.Management => GraphCallKind.Management,
                        _ => GraphCallKind.Other,
                    },
                    Recipient = request.Recipient,
                    Retryable = request.Retryable,
                    Operation = request.Operation ?? "raw",
                    Content = request.Body is { } body ? GraphContent.Json(body) : null,
                },
                responseTypeInfo,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <remarks>
    /// The account and app ids are only demanded when the path actually asks for one, so a
    /// tenant that configured neither can still make a call that needs neither — and one that
    /// does get the error naming the setting rather than a path with a brace still in it.
    /// </remarks>
    private static string Expand(string path, WhatsAppCredentials credentials)
    {
        path = path.Replace(PhoneNumberPlaceholder, credentials.PhoneNumberId, StringComparison.Ordinal);

        if (path.Contains(BusinessAccountPlaceholder, StringComparison.Ordinal))
        {
            path = path.Replace(
                BusinessAccountPlaceholder,
                GraphApiClient.RequireBusinessAccount(credentials),
                StringComparison.Ordinal);
        }

        if (path.Contains(AppPlaceholder, StringComparison.Ordinal))
        {
            path = path.Replace(
                AppPlaceholder,
                GraphApiClient.RequireApp(credentials),
                StringComparison.Ordinal);
        }

        return path;
    }
}
