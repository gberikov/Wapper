using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Wapper.Internal;

/// <summary>
/// Binds a tenant from the <c>WhatsApp</c> section without anybody having named that section.
/// </summary>
/// <remarks>
/// <para>
/// What makes <c>AddWhatsApp()</c> work on its own. It runs per tenant name rather than per
/// registration, so a tenant is bound the first time it is asked for — which is the only way
/// to serve <c>For("acme")</c> when nothing enumerated the tenants at startup.
/// </para>
/// <para>
/// The configuration is optional. A bare service collection with no configuration at all is
/// a perfectly good way to use this library from a test or a console application, and
/// demanding one would turn that into a resolve-time failure.
/// </para>
/// </remarks>
internal sealed class WhatsAppConventionBinder(IConfiguration? configuration)
    : IConfigureNamedOptions<WhatsAppOptions>
{
    public void Configure(WhatsAppOptions options) => Configure(WhatsAppTenant.Default, options);

    public void Configure(string? name, WhatsAppOptions options)
    {
        if (configuration is null)
        {
            return;
        }

        var section = configuration.GetSection(WhatsAppOptions.SectionName);

        if (!section.Exists())
        {
            return;
        }

        // Everything shared, then the tenant's own over the top of it. The default tenant is
        // the section itself and has nothing further to read.
        section.Bind(options);

        if (!string.IsNullOrEmpty(name))
        {
            section.GetSection(WhatsAppOptions.TenantsSectionName).GetSection(name).Bind(options);
        }
    }
}
