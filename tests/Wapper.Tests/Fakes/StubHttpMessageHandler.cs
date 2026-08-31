namespace Wapper.Tests.Fakes;

/// <summary>
/// Returns a scripted response and records what was sent. Thirty lines beat a mock
/// server: no sockets, no ports, no ordering surprises on CI.
/// </summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    : HttpMessageHandler
{
    /// <summary>Every request that reached the handler, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>
    /// The body of each request, captured while the request was still alive. The sender
    /// disposes the request message as soon as the call returns, so reading the content
    /// afterwards would throw.
    /// </summary>
    public List<string?> Bodies { get; } = [];

    public static StubHttpMessageHandler Returning(
        HttpStatusCode status,
        string body,
        string mediaType = "application/json") =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        });

    /// <summary>
    /// Returns each response in turn, repeating the last one once the script runs out. Used
    /// to script a rejection followed by a success.
    /// </summary>
    public static StubHttpMessageHandler Sequence(params (HttpStatusCode Status, string Body)[] responses)
    {
        var index = 0;

        return new StubHttpMessageHandler(_ =>
        {
            var (status, body) = responses[Math.Min(index++, responses.Length - 1)];

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        });
    }

    /// <summary>Returns a response carrying one header, for the usage-header paths.</summary>
    public static StubHttpMessageHandler ReturningWithHeader(
        HttpStatusCode status,
        string body,
        string headerName,
        string headerValue) =>
        new(_ =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            response.Headers.TryAddWithoutValidation(headerName, headerValue);
            return response;
        });

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken));

        return respond(request);
    }
}
