namespace Wapper.Flows;

/// <summary>Where a Flow is in its life.</summary>
public enum FlowStatus
{
    /// <summary>A state this library does not know about yet.</summary>
    Unknown,

    /// <summary>
    /// Still being built. It can only be sent in draft mode, for testing.
    /// </summary>
    Draft,

    /// <summary>
    /// Live, and sendable to customers. A published Flow cannot be deleted, and editing it
    /// puts it back into <see cref="Draft"/> until it is published again.
    /// </summary>
    Published,

    /// <summary>
    /// Retired on purpose, because a published Flow cannot be deleted. Nobody can send or open
    /// it, which is what lets an endpoint be switched off. There is no way back.
    /// </summary>
    Deprecated,

    /// <summary>
    /// Stopped by Meta's monitoring because the endpoint is unhealthy. It cannot be sent or
    /// opened until the endpoint recovers.
    /// </summary>
    Blocked,

    /// <summary>
    /// Held back by Meta's monitoring because the endpoint is struggling. It still opens, but
    /// only ten messages carrying it go out per hour.
    /// </summary>
    Throttled,
}

/// <summary>What a Flow is for. Meta asks for at least one.</summary>
public enum FlowCategory
{
    /// <summary>A category this library does not know about yet.</summary>
    Unknown,

    /// <summary>Registering for something.</summary>
    SignUp,

    /// <summary>Signing in to something.</summary>
    SignIn,

    /// <summary>Booking an appointment.</summary>
    AppointmentBooking,

    /// <summary>Collecting leads.</summary>
    LeadGeneration,

    /// <summary>A contact form.</summary>
    ContactUs,

    /// <summary>Customer support.</summary>
    CustomerSupport,

    /// <summary>A survey.</summary>
    Survey,

    /// <summary>Anything the other categories do not cover.</summary>
    Other,
}

/// <summary>Whether a thing involved in sending a Flow is in a state to do so.</summary>
public enum MessagingAvailability
{
    /// <summary>A state this library does not know about yet.</summary>
    Unknown,

    /// <summary>Everything it needs is in place.</summary>
    Available,

    /// <summary>Usable, with caveats that are spelled out alongside.</summary>
    Limited,

    /// <summary>Something is missing, and the errors alongside say what.</summary>
    Blocked,
}

/// <summary>Something wrong with a Flow's JSON, and where in the file it is.</summary>
/// <remarks>
/// These arrive on a <c>200</c> alongside <c>"success": true</c>. A Flow whose JSON has
/// validation errors is stored, and cannot be published until they are gone.
/// </remarks>
public sealed record FlowValidationError
{
    /// <summary>Meta's name for the problem, for example <c>INVALID_PROPERTY_VALUE</c>.</summary>
    public required string Error { get; init; }

    /// <summary>Which family it belongs to, for example <c>FLOW_JSON_ERROR</c>.</summary>
    public string? ErrorType { get; init; }

    /// <summary>What is wrong, in prose.</summary>
    public string? Message { get; init; }

    /// <summary>First line of the offending text, counting from one.</summary>
    public int? LineStart { get; init; }

    /// <summary>Last line of the offending text.</summary>
    public int? LineEnd { get; init; }

    /// <summary>First column of the offending text.</summary>
    public int? ColumnStart { get; init; }

    /// <summary>Last column of the offending text.</summary>
    public int? ColumnEnd { get; init; }

    /// <summary>
    /// Where in the document, as paths like <c>screens[0].layout.children[0].type</c>.
    /// </summary>
    public IReadOnlyList<string> Paths { get; init; } = [];
}

/// <summary>A shareable rendering of a Flow, for looking at before it goes live.</summary>
public sealed record FlowPreview
{
    /// <summary>
    /// The preview page. It needs no login, so it can be handed to anyone — including anyone
    /// it was not meant for.
    /// </summary>
    /// <remarks>
    /// Add <c>interactive=true</c> to click through it, and <c>flow_token</c>,
    /// <c>flow_action</c> and <c>phone_number</c> to exercise a Flow that has an endpoint.
    /// </remarks>
    public required Uri Url { get; init; }

    /// <summary>When the link stops working. Thirty days from when it was made.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>One thing that has to be healthy for a Flow to be sent.</summary>
public sealed record FlowHealthEntity
{
    /// <summary><c>FLOW</c>, <c>WABA</c>, <c>BUSINESS</c> or <c>APP</c>.</summary>
    public string? EntityType { get; init; }

    /// <summary>Identifier of that thing.</summary>
    public string? Id { get; init; }

    /// <summary>Whether it is in a state to send.</summary>
    public MessagingAvailability CanSendMessage { get; init; }

    /// <summary>What is wrong, when it is blocked.</summary>
    public IReadOnlyList<FlowHealthError> Errors { get; init; } = [];

    /// <summary>What the caveats are, when it is only limited.</summary>
    public IReadOnlyList<string> AdditionalInfo { get; init; } = [];
}

/// <summary>Something standing between a Flow and being sent.</summary>
public sealed record FlowHealthError
{
    /// <summary>Meta's error code.</summary>
    public int Code { get; init; }

    /// <summary>What is wrong.</summary>
    public string? Description { get; init; }

    /// <summary>Where to read about fixing it.</summary>
    public string? PossibleSolution { get; init; }
}

/// <summary>Whether a Flow can be sent, and what is stopping it if not.</summary>
/// <remarks>
/// Sending a Flow involves the Flow, the WhatsApp Business Account, the business portfolio and
/// the Meta app, and any of the four can be the reason a send is refused. This is the answer
/// to "why will it not publish" that <see cref="Flow.ValidationErrors"/> does not give.
/// </remarks>
public sealed record FlowHealth
{
    /// <summary>The verdict over all of them.</summary>
    public MessagingAvailability CanSendMessage { get; init; }

    /// <summary>Each thing involved, and its own verdict.</summary>
    public IReadOnlyList<FlowHealthEntity> Entities { get; init; } = [];
}

/// <summary>A file attached to a Flow.</summary>
public sealed record FlowAsset
{
    /// <summary>Its name. In practice always <c>flow.json</c>.</summary>
    public string? Name { get; init; }

    /// <summary>Its kind. In practice always <c>FLOW_JSON</c>.</summary>
    public string? AssetType { get; init; }

    /// <summary>Where the content can be fetched from. Needs no token, and does not last.</summary>
    public string? DownloadUrl { get; init; }
}

/// <summary>A Flow: a form a customer fills in inside WhatsApp.</summary>
public sealed record Flow
{
    /// <summary>Identifier assigned by Meta.</summary>
    public required string Id { get; init; }

    /// <summary>The name it was given. Never shown to a customer.</summary>
    public string? Name { get; init; }

    /// <summary>Where it is in its life.</summary>
    public FlowStatus Status { get; init; }

    /// <summary>What it is for.</summary>
    public IReadOnlyList<FlowCategory> Categories { get; init; } = [];

    /// <summary>The categories exactly as Meta wrote them, for ones not known here yet.</summary>
    public IReadOnlyList<string> RawCategories { get; init; } = [];

    /// <summary>
    /// What is wrong with its JSON. All of it has to be gone before the Flow can be published.
    /// </summary>
    public IReadOnlyList<FlowValidationError> ValidationErrors { get; init; } = [];

    /// <summary>Version declared in the Flow JSON.</summary>
    public string? JsonVersion { get; init; }

    /// <summary>Version of the Data API declared in the Flow JSON. Only for Flows with an endpoint.</summary>
    public string? DataApiVersion { get; init; }

    /// <summary>
    /// The endpoint the Flow talks to while a customer fills it in.
    /// </summary>
    /// <remarks>
    /// From Flow JSON version 3.0 this can only be set through the API or the builder, not in
    /// the JSON itself.
    /// </remarks>
    public Uri? EndpointUri { get; init; }

    /// <summary>A shareable rendering, when it was asked for.</summary>
    public FlowPreview? Preview { get; init; }

    /// <summary>Whether it can be sent, when it was asked for.</summary>
    public FlowHealth? Health { get; init; }
}

/// <summary>A new Flow.</summary>
public sealed record FlowDefinition
{
    /// <summary>What to call it. Not shown to customers.</summary>
    public required string Name { get; init; }

    /// <summary>What it is for. At least one.</summary>
    public required IReadOnlyList<FlowCategory> Categories { get; init; }

    /// <summary>
    /// The Flow JSON — the screens and their layout.
    /// </summary>
    /// <remarks>
    /// Optional: a Flow can be created empty and have its JSON uploaded afterwards. Sent as a
    /// string containing JSON, which is Meta's shape, not a nested object.
    /// </remarks>
    public string? Json { get; init; }

    /// <summary>
    /// Whether to publish it straight away. Only works when <see cref="Json"/> is given and
    /// valid.
    /// </summary>
    public bool Publish { get; init; }

    /// <summary>
    /// Copy an existing Flow instead of starting empty. The token has to be able to read it.
    /// </summary>
    public string? CloneFlowId { get; init; }

    /// <summary>The endpoint the Flow talks to, for a Flow that has one.</summary>
    public Uri? EndpointUri { get; init; }
}

/// <summary>What Meta said about a newly created Flow.</summary>
public sealed record FlowCreationResult
{
    /// <summary>Identifier of the new Flow.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// What is wrong with its JSON.
    /// </summary>
    /// <remarks>
    /// The Flow exists either way — these arrive on a success, not on a failure. A Flow with
    /// validation errors cannot be published.
    /// </remarks>
    public IReadOnlyList<FlowValidationError> ValidationErrors { get; init; } = [];
}

/// <summary>Changes to a Flow's metadata. Anything left unset is left alone.</summary>
public sealed record FlowUpdate
{
    /// <summary>A new name.</summary>
    public string? Name { get; init; }

    /// <summary>New categories, replacing the old ones. At least one if given at all.</summary>
    public IReadOnlyList<FlowCategory>? Categories { get; init; }

    /// <summary>A new endpoint.</summary>
    public Uri? EndpointUri { get; init; }

    /// <summary>
    /// The Meta app to connect. Every Flow with an endpoint needs one connected before it can
    /// be published.
    /// </summary>
    public string? ApplicationId { get; init; }
}
