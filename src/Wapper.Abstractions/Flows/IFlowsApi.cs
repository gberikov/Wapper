namespace Wapper.Flows;

/// <summary>
/// Building, publishing and retiring the Flows of a WhatsApp Business Account.
/// </summary>
/// <remarks>
/// <para>
/// These calls are billed against the account's management allowance, which the client paces
/// for you, and every one of them needs
/// <see cref="WhatsAppCredentials.WhatsAppBusinessAccountId"/>.
/// </para>
/// <para>
/// A Flow's life runs one way: draft, published, deprecated. Only a draft can be deleted, and
/// a published Flow that is edited drops back to draft until it is published again.
/// </para>
/// </remarks>
public interface IFlowsApi
{
    /// <summary>
    /// Lists the account's Flows, fetching further pages as they are read.
    /// </summary>
    /// <remarks>
    /// Carries the fields Meta returns by default — id, name, status, categories and
    /// validation errors. The preview link and the health status are per-Flow work, so they
    /// only come back from <see cref="GetAsync"/>.
    /// </remarks>
    IAsyncEnumerable<Flow> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one Flow, with everything Meta will say about it.</summary>
    /// <param name="flowId">Which Flow.</param>
    /// <param name="healthCheckPhoneNumberId">
    /// Check whether this particular number could send the Flow, rather than only whether the
    /// Flow itself is in a state to be sent.
    /// </param>
    /// <param name="includePreview">
    /// Whether to fetch the shareable preview link as well. Off by default: the link needs no
    /// login and lasts thirty days, so it is not something to pull into every read and every
    /// log line that follows. <see cref="GetPreviewAsync"/> asks for it on its own.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<Flow> GetAsync(
        string flowId,
        string? healthCheckPhoneNumberId = null,
        bool includePreview = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a Flow.
    /// </summary>
    /// <remarks>
    /// New Flows start as drafts. Meta answers with <c>200</c> and the new id even when the
    /// JSON is invalid, listing the problems in
    /// <see cref="FlowCreationResult.ValidationErrors"/> — so a caller that only checks for an
    /// exception will believe a broken Flow is fine right up until it refuses to publish.
    /// </remarks>
    Task<FlowCreationResult> CreateAsync(
        FlowDefinition flow,
        CancellationToken cancellationToken = default);

    /// <summary>Renames a Flow, re-categorises it, or points it at another endpoint.</summary>
    Task UpdateAsync(
        string flowId,
        FlowUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the Flow JSON.
    /// </summary>
    /// <param name="flowId">Which Flow.</param>
    /// <param name="json">The document. At most 10 MB, and read once.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// What is wrong with the document. Empty when there is nothing wrong. As with
    /// <see cref="CreateAsync"/> these arrive on a success: the upload is accepted either way,
    /// and it is publishing that fails later.
    /// </returns>
    /// <remarks>
    /// Uploaded as multipart form data rather than as a JSON body, which is Meta's shape for
    /// this one endpoint. Editing a published Flow drops it back to draft.
    /// </remarks>
    Task<IReadOnlyList<FlowValidationError>> UpdateJsonAsync(
        string flowId,
        Stream json,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="UpdateJsonAsync(string, Stream, CancellationToken)" />
    Task<IReadOnlyList<FlowValidationError>> UpdateJsonAsync(
        string flowId,
        string json,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a Flow, making it sendable to customers.
    /// </summary>
    /// <remarks>
    /// Needs every validation error resolved, the business verified, and — for a Flow with an
    /// endpoint — a Meta app connected. A published Flow can no longer be deleted.
    /// </remarks>
    Task PublishAsync(string flowId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires a published Flow.
    /// </summary>
    /// <remarks>
    /// The only way to take a published Flow out of service, since it cannot be deleted. It
    /// stops being sendable and openable, which is what lets its endpoint be switched off.
    /// There is no way back.
    /// </remarks>
    Task DeprecateAsync(string flowId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a draft Flow.
    /// </summary>
    /// <remarks>
    /// Only a draft. A published Flow is retired with <see cref="DeprecateAsync"/> instead.
    /// </remarks>
    Task DeleteAsync(string flowId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a shareable rendering of the Flow.
    /// </summary>
    /// <param name="flowId">Which Flow.</param>
    /// <param name="invalidate">
    /// Whether to throw the current link away and mint a new one. Do this when a link has been
    /// shared with someone who should no longer have it — it needs no login.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<FlowPreview> GetPreviewAsync(
        string flowId,
        bool invalidate = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the files attached to a Flow, and where to download each one.
    /// </summary>
    /// <remarks>This is how the current Flow JSON is read back.</remarks>
    Task<IReadOnlyList<FlowAsset>> ListAssetsAsync(
        string flowId,
        CancellationToken cancellationToken = default);
}
