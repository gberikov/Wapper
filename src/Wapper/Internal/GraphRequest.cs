namespace Wapper.Internal;

/// <summary>What a call spends, which decides how it is paced.</summary>
internal enum GraphCallKind
{
    /// <summary>
    /// Not governed by a documented budget. Media upload and download sit here: Meta does
    /// not list them against the business account allowance, and pacing them against a
    /// number that was never published would be guesswork.
    /// </summary>
    Other,

    /// <summary>
    /// Sending a message. Spends the throughput of the business phone number, and the pair
    /// allowance of the conversation when the recipient is known.
    /// </summary>
    Message,

    /// <summary>
    /// A management call — templates, subscribed apps, phone numbers. Spends the hourly
    /// allowance of the WhatsApp Business Account.
    /// </summary>
    Management,
}

/// <summary>One call to the Graph API, described well enough to pace and to retry.</summary>
internal sealed record GraphRequest
{
    /// <summary>Tenant whose options supply the base address, API version and allowances.</summary>
    public required string Tenant { get; init; }

    /// <summary>Credentials to present.</summary>
    public required WhatsAppCredentials Credentials { get; init; }

    /// <summary>HTTP method.</summary>
    public required HttpMethod Method { get; init; }

    /// <summary>Path below the API version, for example <c>123456/messages</c>.</summary>
    public required string Path { get; init; }

    /// <summary>
    /// Builds the request body, or <see langword="null"/> when there is none.
    /// </summary>
    /// <remarks>
    /// A factory rather than a body, because a retry needs a fresh one: the content of the
    /// first attempt has already been read to the wire, and a stream cannot be rewound.
    /// </remarks>
    public Func<HttpContent>? Content { get; init; }

    /// <summary>What the call spends.</summary>
    public GraphCallKind Kind { get; init; } = GraphCallKind.Other;

    /// <summary>
    /// The recipient, for a message. Needed for the pair allowance, which is counted per
    /// conversation rather than per phone number.
    /// </summary>
    public string? Recipient { get; init; }
}
