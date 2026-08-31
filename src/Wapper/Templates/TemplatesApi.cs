using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Wapper.Internal;

namespace Wapper.Templates;

/// <summary>Managing the templates of one tenant's WhatsApp Business Account.</summary>
internal sealed class TemplatesApi(GraphApiClient client, string tenant) : ITemplatesApi
{
    /// <summary>Meta refuses more than this many ids on one delete.</summary>
    private const int MaxBulkDelete = 100;

    public async IAsyncEnumerable<Template> ListAsync(
        TemplateQuery? query = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var credentials = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        var accountId = RequireAccount(credentials);
        string? after = null;

        do
        {
            var page = await client.SendAsync(
                    new GraphRequest
                    {
                        Tenant = tenant,
                        Credentials = credentials,
                        Method = HttpMethod.Get,
                        Path = $"{accountId}/message_templates{BuildQuery(query, after)}",
                        Kind = GraphCallKind.Management,
                    },
                    WhatsAppJsonContext.Default.TemplateListResponse,
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (var item in page.Data ?? [])
            {
                yield return item.ToTemplate();
            }

            // Meta signals the last page by leaving out `next`, not by sending an empty
            // cursor: it keeps sending a cursor that would fetch the same page again.
            after = page.Paging?.Next is null ? null : page.Paging.Cursors?.After;
        }
        while (!string.IsNullOrEmpty(after));
    }

    public async Task<Template> GetAsync(
        string templateId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        var credentials = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        var payload = await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Get,
                    Path = templateId,
                    Kind = GraphCallKind.Management,
                },
                WhatsAppJsonContext.Default.TemplateDefinitionPayload,
                cancellationToken)
            .ConfigureAwait(false);

        return payload.ToTemplate();
    }

    public async Task<TemplateCreationResult> CreateAsync(
        Template template,
        bool allowCategoryChange = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        GuardName(template.Name);

        var credentials = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        var accountId = RequireAccount(credentials);
        var payload = template.ToPayload(allowCategoryChange);

        var response = await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    Path = $"{accountId}/message_templates",
                    Kind = GraphCallKind.Management,
                    Content = () => JsonContent.Create(
                        payload,
                        WhatsAppJsonContext.Default.TemplateDefinitionPayload),
                },
                WhatsAppJsonContext.Default.TemplateCreatedResponse,
                cancellationToken)
            .ConfigureAwait(false);

        return new TemplateCreationResult
        {
            Id = response.Id ?? throw new WhatsAppException(
                "The Cloud API accepted the template but returned no id, so there is nothing " +
                "to match its review outcome against."),
            Status = TemplateMapping.ParseStatus(response.Status),
            // Not necessarily the category that was asked for: with allowCategoryChange set,
            // Meta files a template it considers miscategorised under the right one instead
            // of rejecting it.
            Category = TemplateMapping.ParseCategory(response.Category),
        };
    }

    public async Task UpdateAsync(
        string templateId,
        Template template,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentNullException.ThrowIfNull(template);

        var credentials = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        // Only the components go up. Name, language and category are not editable this way,
        // and sending them makes the call fail rather than being ignored.
        var payload = new TemplateDefinitionPayload
        {
            Components = template.ToPayload(allowCategoryChange: null).Components,
            MessageSendTtlSeconds = template.TimeToLive is { } ttl ? (int)ttl.TotalSeconds : null,
        };

        await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    Path = templateId,
                    Kind = GraphCallKind.Management,
                    Content = () => JsonContent.Create(
                        payload,
                        WhatsAppJsonContext.Default.TemplateDefinitionPayload),
                },
                WhatsAppJsonContext.Default.SuccessResponse,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpdateCategoryAsync(
        string templateId,
        TemplateCategory category,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        var credentials = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        var payload = new TemplateDefinitionPayload { Category = TemplateMapping.ToWire(category) };

        await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Post,
                    Path = templateId,
                    Kind = GraphCallKind.Management,
                    Content = () => JsonContent.Create(
                        payload,
                        WhatsAppJsonContext.Default.TemplateDefinitionPayload),
                },
                WhatsAppJsonContext.Default.SuccessResponse,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task DeleteByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return DeleteAsync($"name={Uri.EscapeDataString(name)}", cancellationToken);
    }

    public Task DeleteAsync(
        string templateId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        // Meta wants the name alongside the id, and deletes by name alone if it is missing --
        // which would take every language with it.
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return DeleteAsync(
            $"hsm_id={Uri.EscapeDataString(templateId)}&name={Uri.EscapeDataString(name)}",
            cancellationToken);
    }

    public Task DeleteAsync(
        IEnumerable<string> templateIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(templateIds);

        var ids = templateIds.ToList();

        if (ids.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (ids.Count > MaxBulkDelete)
        {
            throw new ArgumentException(
                $"The Cloud API takes at most {MaxBulkDelete} template ids per delete, and this " +
                $"call passed {ids.Count}. Send them in batches.",
                nameof(templateIds));
        }

        // A JSON-looking array in a query parameter, which is how Meta specifies this one.
        return DeleteAsync(
            $"hsm_ids={Uri.EscapeDataString($"[{string.Join(',', ids)}]")}",
            cancellationToken);
    }

    private async Task DeleteAsync(string query, CancellationToken cancellationToken)
    {
        var credentials = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        var accountId = RequireAccount(credentials);

        await client.SendAsync(
                new GraphRequest
                {
                    Tenant = tenant,
                    Credentials = credentials,
                    Method = HttpMethod.Delete,
                    Path = $"{accountId}/message_templates?{query}",
                    Kind = GraphCallKind.Management,
                },
                WhatsAppJsonContext.Default.SuccessResponse,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string BuildQuery(TemplateQuery? query, string? after)
    {
        var parts = new List<string>(5);

        if (query?.Name is { } name)
        {
            parts.Add($"name={Uri.EscapeDataString(name)}");
        }

        if (query?.Status is { } status)
        {
            parts.Add($"status={Uri.EscapeDataString(TemplateMapping.ToWire(status))}");
        }

        if (query?.Category is { } category)
        {
            parts.Add($"category={Uri.EscapeDataString(TemplateMapping.ToWire(category))}");
        }

        if (query?.Language is { } language)
        {
            parts.Add($"language={Uri.EscapeDataString(language)}");
        }

        if (query?.PageSize is { } pageSize)
        {
            parts.Add($"limit={pageSize}");
        }

        if (!string.IsNullOrEmpty(after))
        {
            parts.Add($"after={Uri.EscapeDataString(after)}");
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }

    private static void GuardName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Meta answers a bad name with a bare code 100, which says nothing about what was
        // wrong with it.
        if (name.Length > 512)
        {
            throw new ArgumentException(
                $"A template name is at most 512 characters, and this one is {name.Length}.",
                nameof(name));
        }

        foreach (var character in name)
        {
            if (character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')
            {
                continue;
            }

            throw new ArgumentException(
                $"A template name may only contain lowercase letters, digits and underscores, " +
                $"and '{name}' contains '{character}'.",
                nameof(name));
        }
    }

    private static string RequireAccount(WhatsAppCredentials credentials) =>
        credentials.WhatsAppBusinessAccountId
        ?? throw new WhatsAppConfigurationException(
            "Managing templates needs the WhatsApp Business Account id. Set " +
            "WhatsApp:WhatsAppBusinessAccountId, or return it from your " +
            $"{nameof(IWhatsAppCredentialsProvider)}.");

    private ValueTask<WhatsAppCredentials> ResolveAsync(CancellationToken cancellationToken) =>
        client.ResolveCredentialsAsync(tenant, cancellationToken);
}
