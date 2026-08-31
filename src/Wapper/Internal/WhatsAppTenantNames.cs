namespace Wapper.Internal;

/// <summary>
/// The tenant names registered in code.
/// </summary>
/// <remarks>
/// <para>
/// <c>IOptionsMonitor</c> resolves a tenant by name and cannot be asked which names exist, so
/// anything that has to look at every tenant — resolving a webhook delivery to one, for
/// instance — needs the list kept separately.
/// </para>
/// <para>
/// Written while the container is being built and read only once it is running, which is why
/// it is registered as an instance and needs no locking.
/// </para>
/// <para>
/// Names that appear only in configuration are not here. <c>AddWhatsApp()</c> binds a tenant
/// the first time it is asked for and never enumerates them, so a tenant added to a config map
/// after startup has no registration to have been part of; a reader unions this with what the
/// configuration says.
/// </para>
/// </remarks>
internal sealed class WhatsAppTenantNames
{
    private readonly HashSet<string> names = new(StringComparer.Ordinal) { WhatsAppTenant.Default };

    /// <summary>Records a tenant registered in code.</summary>
    public void Add(string tenant) => names.Add(tenant);

    /// <summary>Whether a name has already been recorded.</summary>
    public bool Contains(string tenant) => names.Contains(tenant);

    /// <summary>The recorded names, in no particular order.</summary>
    public IReadOnlyCollection<string> Registered => names;
}
