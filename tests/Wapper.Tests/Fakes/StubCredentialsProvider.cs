namespace Wapper.Tests.Fakes;

/// <summary>Returns fixed credentials, whatever the tenant.</summary>
internal sealed class StubCredentialsProvider(WhatsAppCredentials credentials)
    : IWhatsAppCredentialsProvider
{
    public ValueTask<WhatsAppCredentials> GetCredentialsAsync(
        string tenant,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(credentials);
}
