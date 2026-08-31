using Wapper.Internal;

namespace Wapper.Accounts;

/// <summary>One tenant's WhatsApp Business Account.</summary>
internal sealed class WhatsAppAccountApi(GraphApiClient client, string tenant) : IWhatsAppAccountApi
{
    public async Task<IReadOnlyList<SubscribedApp>> GetSubscribedAppsAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
                HttpMethod.Get,
                "account.get_subscribed_apps",
                WhatsAppJsonContext.Default.SubscribedAppListResponse,
                cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. (response.Data ?? [])
                .Select(app => app.Data)
                .OfType<SubscribedAppDataPayload>()
                .Select(app => new SubscribedApp
                {
                    Id = app.Id,
                    Name = app.Name,
                    Link = app.Link,
                }),
        ];
    }

    public Task SubscribeAsync(CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Post,
            "account.subscribe",
            WhatsAppJsonContext.Default.SuccessResponse,
            cancellationToken);

    public Task UnsubscribeAsync(CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Delete,
            "account.unsubscribe",
            WhatsAppJsonContext.Default.SuccessResponse,
            cancellationToken);

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string operation,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResponse> typeInfo,
        CancellationToken cancellationToken)
    {
        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);
        var accountId = GraphApiClient.RequireBusinessAccount(credentials);

        return await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = method,
                    Path = $"{accountId}/subscribed_apps",
                    Kind = GraphCallKind.Management,
                    Operation = operation,
                },
                typeInfo,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
