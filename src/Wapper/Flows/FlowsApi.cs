using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using Wapper.Internal;

namespace Wapper.Flows;

/// <summary>The Flows of one tenant's WhatsApp Business Account.</summary>
internal sealed class FlowsApi(GraphApiClient client, string tenant) : IFlowsApi
{
    /// <summary>
    /// Everything Meta will say about one Flow.
    /// </summary>
    /// <remarks>
    /// Only id, name, status, categories and validation errors come back by default. Asking
    /// for the preview with <c>invalidate(false)</c> returns the link that already exists
    /// rather than minting a new one and breaking the old.
    /// </remarks>
    private const string Fields =
        "id,name,status,categories,validation_errors,json_version,data_api_version," +
        "endpoint_uri,preview.invalidate(false)";

    /// <summary>Meta insists on both of these values, exactly.</summary>
    private const string AssetName = "flow.json";
    private const string AssetType = "FLOW_JSON";

    public async IAsyncEnumerable<Flow> ListAsync(
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
                        // No field list: the defaults are what a listing wants, and asking for
                        // the preview or the health status here would be per-Flow work on
                        // every Flow of the account.
                        Path = $"{accountId}/flows" +
                               (after is null ? string.Empty : $"?after={Uri.EscapeDataString(after)}"),
                        Kind = GraphCallKind.Management,
                    },
                    WhatsAppJsonContext.Default.FlowListResponse,
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (var item in page.Data ?? [])
            {
                yield return item.ToFlow();
            }

            after = page.Paging?.NextCursor;
        }
        while (!string.IsNullOrEmpty(after));
    }

    public async Task<Flow> GetAsync(
        string flowId,
        string? healthCheckPhoneNumberId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flowId);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        // The health status is asked for either bare, or narrowed to one phone number so that
        // the answer is "could this number send it" rather than "is the Flow itself sendable".
        var health = string.IsNullOrWhiteSpace(healthCheckPhoneNumberId)
            ? "health_status"
            : $"health_status.phone_number({Uri.EscapeDataString(healthCheckPhoneNumberId)})";

        var payload = await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Get,
                    Path = $"{flowId}?fields={Fields},{health}",
                    Kind = GraphCallKind.Management,
                },
                WhatsAppJsonContext.Default.FlowPayload,
                cancellationToken)
            .ConfigureAwait(false);

        return payload.ToFlow();
    }

    public async Task<FlowCreationResult> CreateAsync(
        FlowDefinition flow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentException.ThrowIfNullOrWhiteSpace(flow.Name);

        if (flow.Categories is not { Count: > 0 })
        {
            throw new ArgumentException(
                "A Flow needs at least one category.",
                nameof(flow));
        }

        if (flow.Publish && string.IsNullOrWhiteSpace(flow.Json))
        {
            // Meta ignores the flag rather than refusing the request, so the Flow would be
            // created as a draft and nobody would be told why.
            throw new ArgumentException(
                "A Flow can only be published as it is created if its JSON is sent with it.",
                nameof(flow));
        }

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);
        var accountId = GraphApiClient.RequireBusinessAccount(credentials);
        var payload = new FlowDefinitionPayload
        {
            Name = flow.Name,
            Categories = [.. flow.Categories.Select(FlowMapping.ToWire)],
            FlowJson = flow.Json,
            Publish = flow.Publish ? true : null,
            CloneFlowId = flow.CloneFlowId,
            EndpointUri = flow.EndpointUri?.ToString(),
        };

        var response = await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    Path = $"{accountId}/flows",
                    Kind = GraphCallKind.Management,
                    Content = () => JsonContent.Create(
                        payload,
                        WhatsAppJsonContext.Default.FlowDefinitionPayload),
                },
                WhatsAppJsonContext.Default.FlowWriteResponse,
                cancellationToken)
            .ConfigureAwait(false);

        return new FlowCreationResult
        {
            Id = response.Id ?? throw new WhatsAppException(
                "The Cloud API accepted the Flow but returned no id, so there is nothing to " +
                "publish or edit later."),
            // Present on a success, not on a failure: the Flow exists, and simply cannot be
            // published until these are gone.
            ValidationErrors = FlowMapping.ToValidationErrors(response.ValidationErrors),
        };
    }

    public async Task UpdateAsync(
        string flowId,
        FlowUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flowId);
        ArgumentNullException.ThrowIfNull(update);

        if (update.Categories is { Count: 0 })
        {
            throw new ArgumentException(
                "Leave the categories unset to keep the Flow's current ones. An empty list is " +
                "not something the Cloud API accepts.",
                nameof(update));
        }

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);
        var payload = new FlowDefinitionPayload
        {
            Name = update.Name,
            Categories = update.Categories is { } categories
                ? [.. categories.Select(FlowMapping.ToWire)]
                : null,
            EndpointUri = update.EndpointUri?.ToString(),
            ApplicationId = update.ApplicationId,
        };

        await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    Path = flowId,
                    Kind = GraphCallKind.Management,
                    Content = () => JsonContent.Create(
                        payload,
                        WhatsAppJsonContext.Default.FlowDefinitionPayload),
                },
                WhatsAppJsonContext.Default.SuccessResponse,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FlowValidationError>> UpdateJsonAsync(
        string flowId,
        Stream json,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flowId);
        ArgumentNullException.ThrowIfNull(json);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        var response = await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    Path = $"{flowId}/assets",
                    Kind = GraphCallKind.Management,
                    // Form data, not a JSON body, for this one endpoint.
                    Content = () =>
                    {
                        var file = new StreamContent(json);
                        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                        return new MultipartFormDataContent
                        {
                            { file, "file", AssetName },
                            { new StringContent(AssetName), "name" },
                            { new StringContent(AssetType), "asset_type" },
                        };
                    },
                    // The stream has already been read by the time a retry would happen, and
                    // sending it again would upload an empty file.
                    Retryable = false,
                },
                WhatsAppJsonContext.Default.FlowWriteResponse,
                cancellationToken)
            .ConfigureAwait(false);

        return FlowMapping.ToValidationErrors(response.ValidationErrors);
    }

    public Task<IReadOnlyList<FlowValidationError>> UpdateJsonAsync(
        string flowId,
        string json,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(json);

        return UpdateJsonAsync(
            flowId,
            new MemoryStream(Encoding.UTF8.GetBytes(json)),
            cancellationToken);
    }

    public Task PublishAsync(string flowId, CancellationToken cancellationToken = default) =>
        PostAsync(flowId, "publish", cancellationToken);

    public Task DeprecateAsync(string flowId, CancellationToken cancellationToken = default) =>
        PostAsync(flowId, "deprecate", cancellationToken);

    public async Task DeleteAsync(string flowId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flowId);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Delete,
                    Path = flowId,
                    Kind = GraphCallKind.Management,
                },
                WhatsAppJsonContext.Default.SuccessResponse,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<FlowPreview> GetPreviewAsync(
        string flowId,
        bool invalidate = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flowId);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        var response = await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Get,
                    // A field with an argument: invalidate(true) throws the current link away
                    // and mints another.
                    Path = $"{flowId}?fields=preview.invalidate({(invalidate ? "true" : "false")})",
                    Kind = GraphCallKind.Management,
                },
                WhatsAppJsonContext.Default.FlowPreviewResponse,
                cancellationToken)
            .ConfigureAwait(false);

        return response.Preview.ToPreview() ?? throw new WhatsAppException(
            $"The Cloud API returned no preview link for Flow {flowId}.");
    }

    public async Task<IReadOnlyList<FlowAsset>> ListAssetsAsync(
        string flowId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flowId);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        var response = await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Get,
                    Path = $"{flowId}/assets",
                    Kind = GraphCallKind.Management,
                },
                WhatsAppJsonContext.Default.FlowAssetListResponse,
                cancellationToken)
            .ConfigureAwait(false);

        return [.. (response.Data ?? []).Select(asset => new FlowAsset
        {
            Name = asset.Name,
            AssetType = asset.AssetType,
            DownloadUrl = asset.DownloadUrl,
        })];
    }

    private async Task PostAsync(string flowId, string edge, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flowId);

        var credentials = await client.ResolveCredentialsAsync(tenant, cancellationToken)
            .ConfigureAwait(false);

        await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    Path = $"{flowId}/{edge}",
                    Kind = GraphCallKind.Management,
                },
                WhatsAppJsonContext.Default.SuccessResponse,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
