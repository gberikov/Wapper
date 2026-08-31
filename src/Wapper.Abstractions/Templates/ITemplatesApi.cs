namespace Wapper.Templates;

/// <summary>What to narrow a template listing to.</summary>
public sealed record TemplateQuery
{
    /// <summary>Only templates with this name, across every language it exists in.</summary>
    public string? Name { get; init; }

    /// <summary>Only templates in this state.</summary>
    public TemplateStatus? Status { get; init; }

    /// <summary>Only templates in this category.</summary>
    public TemplateCategory? Category { get; init; }

    /// <summary>Only templates in this locale.</summary>
    public string? Language { get; init; }

    /// <summary>
    /// How many to ask for per request. Meta decides the default; larger pages mean fewer
    /// requests against the hourly management allowance.
    /// </summary>
    public int? PageSize { get; init; }
}

/// <summary>What Meta said when a template was submitted.</summary>
public sealed record TemplateCreationResult
{
    /// <summary>Identifier of the new template.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Its state. Normally <see cref="TemplateStatus.Pending"/>: review can take up to a day,
    /// and the outcome arrives on the <c>message_template_status_update</c> webhook.
    /// </summary>
    public TemplateStatus Status { get; init; }

    /// <summary>
    /// The category Meta filed it under, which is not always the one that was asked for.
    /// </summary>
    public TemplateCategory Category { get; init; }
}

/// <summary>
/// Creating, reading, editing and deleting the templates of a WhatsApp Business Account.
/// </summary>
/// <remarks>
/// <para>
/// These calls are billed against the account's management allowance — 200 requests an hour,
/// or 5000 once the account has a registered phone number — which the client paces for you.
/// </para>
/// <para>
/// Every method needs <see cref="WhatsAppCredentials.WhatsAppBusinessAccountId"/>. An
/// application that only sends messages never has to configure it; one that manages
/// templates does.
/// </para>
/// </remarks>
public interface ITemplatesApi
{
    /// <summary>
    /// Lists the account's templates, fetching further pages as they are read.
    /// </summary>
    /// <remarks>
    /// Each page is a separate request against the hourly allowance, so enumerate once and
    /// keep what you need rather than re-reading the sequence.
    /// </remarks>
    IAsyncEnumerable<Template> ListAsync(
        TemplateQuery? query = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one template by id.</summary>
    Task<Template> GetAsync(string templateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads the sample image, video or document a media header is reviewed with, and
    /// returns the handle to build the header from.
    /// </summary>
    /// <param name="content">The bytes. Read once, from the current position.</param>
    /// <param name="mimeType">Media type, for example <c>image/png</c>.</param>
    /// <param name="cancellationToken">Cancels the upload.</param>
    /// <returns>
    /// A handle for <see cref="TemplateHeader.FromImage"/>, <see cref="TemplateHeader.FromVideo"/>
    /// or <see cref="TemplateHeader.FromDocument"/>. Not a media id: the two are issued by
    /// different endpoints and are not interchangeable.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Goes through the Resumable Upload API, which is addressed to the Meta app rather than
    /// to the account and so needs <see cref="WhatsAppCredentials.AppId"/> — the only other
    /// thing in this library that does is setting the business profile picture.
    /// </para>
    /// <para>
    /// The file is buffered in memory so the upload can be retried, which is fine for a
    /// sample and wrong for anything large. The sample is reviewed along with the template;
    /// the media actually sent with each message is supplied at send time.
    /// </para>
    /// </remarks>
    Task<string> UploadHeaderSampleAsync(
        Stream content,
        string mimeType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a new template for review.
    /// </summary>
    /// <param name="template">
    /// The template. Its <see cref="Template.Id"/> is ignored; Meta assigns one.
    /// </param>
    /// <param name="allowCategoryChange">
    /// Whether Meta may file it under a category other than the one asked for. Worth leaving
    /// on: without it, a template Meta considers miscategorised is rejected outright rather
    /// than being moved.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// An account may create 100 templates an hour, and hold 250 in total — 6000 once the
    /// business portfolio is verified and one of its numbers has an approved display name.
    /// </remarks>
    Task<TemplateCreationResult> CreateAsync(
        Template template,
        bool allowCategoryChange = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the components of an existing template.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Components are replaced wholesale, not merged: whatever is left out is removed. Only
    /// approved, rejected and paused templates can be edited, and an approved one may be
    /// edited ten times in 30 days and once in any 24 hours.
    /// </para>
    /// <para>
    /// Editing an approved template sends it back through review, which it normally passes
    /// automatically.
    /// </para>
    /// </remarks>
    Task UpdateAsync(
        string templateId,
        Template template,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a template to another category.
    /// </summary>
    /// <remarks>The category of an approved template cannot be changed.</remarks>
    Task UpdateCategoryAsync(
        string templateId,
        TemplateCategory category,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every template with this name.
    /// </summary>
    /// <remarks>
    /// Every language too — the name is shared. Use <see cref="DeleteAsync(string, string, CancellationToken)"/>
    /// to remove one. The name of a deleted approved template cannot be reused for 30 days.
    /// </remarks>
    Task DeleteByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Deletes one template, leaving its other languages alone.</summary>
    /// <param name="templateId">Which template.</param>
    /// <param name="name">Its name, which Meta requires alongside the id.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task DeleteAsync(string templateId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes several templates by id.
    /// </summary>
    /// <remarks>
    /// At most 100 at a time. If any id is invalid the whole request fails and nothing is
    /// deleted, so this is all or nothing.
    /// </remarks>
    Task DeleteAsync(IEnumerable<string> templateIds, CancellationToken cancellationToken = default);
}
