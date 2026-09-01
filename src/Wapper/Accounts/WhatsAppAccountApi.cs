using Wapper.Internal;

namespace Wapper.Accounts;

/// <summary>One tenant's WhatsApp Business Account.</summary>
internal sealed class WhatsAppAccountApi(GraphApiClient client, string tenant) : IWhatsAppAccountApi
{
    public async Task<IReadOnlyList<SubscribedApp>> GetSubscribedAppsAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await client.SendAsync(
                await RequestAsync(HttpMethod.Get, "account.get_subscribed_apps", cancellationToken)
                    .ConfigureAwait(false),
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

    public async Task SubscribeAsync(CancellationToken cancellationToken = default) =>
        await client.SendAsync(
                await RequestAsync(HttpMethod.Post, "account.subscribe", cancellationToken)
                    .ConfigureAwait(false),
                cancellationToken)
            .ConfigureAwait(false);

    public async Task UnsubscribeAsync(CancellationToken cancellationToken = default) =>
        await client.SendAsync(
                await RequestAsync(HttpMethod.Delete, "account.unsubscribe", cancellationToken)
                    .ConfigureAwait(false),
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<GraphRequest> RequestAsync(
        HttpMethod method,
        string operation,
        CancellationToken cancellationToken)
    {
        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);
        var accountId = GraphApiClient.RequireBusinessAccount(credentials);

        return new GraphRequest
        {
            Tenant = tenant,
            Credentials = credentials,
            Method = method,
            Path = $"{accountId}/subscribed_apps",
            Kind = GraphCallKind.Management,
            Operation = operation,
        };
    }
}
