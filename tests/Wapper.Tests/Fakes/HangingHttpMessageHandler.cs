using System.Diagnostics;

namespace Wapper.Tests.Fakes;

/// <summary>
/// Never answers. Stands in for a Cloud API that has stopped responding, which is the only
/// way to reach the per-tenant timeout.
/// </summary>
internal sealed class HangingHttpMessageHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);

        throw new UnreachableException();
    }
}
