using System.Globalization;
using Wapper.Internal;

namespace Wapper.Flows;

/// <summary>Turns the wire shape of a Flow into the model, and back.</summary>
internal static class FlowMapping
{
    internal static Flow ToFlow(this FlowPayload payload) => new()
    {
        Id = payload.Id ?? string.Empty,
        Name = payload.Name,
        Status = ParseStatus(payload.Status),
        Categories = [.. (payload.Categories ?? []).Select(ParseCategory)],
        RawCategories = payload.Categories ?? [],
        ValidationErrors = ToValidationErrors(payload.ValidationErrors),
        JsonVersion = payload.JsonVersion,
        DataApiVersion = payload.DataApiVersion,
        EndpointUri = ParseUri(payload.EndpointUri),
        Preview = payload.Preview.ToPreview(),
        Health = payload.HealthStatus is { } health
            ? new FlowHealth
            {
                CanSendMessage = ParseAvailability(health.CanSendMessage),
                Entities = [.. (health.Entities ?? []).Select(entity => new FlowHealthEntity
                {
                    EntityType = entity.EntityType,
                    Id = entity.Id,
                    CanSendMessage = ParseAvailability(entity.CanSendMessage),
                    Errors = [.. (entity.Errors ?? []).Select(error => new FlowHealthError
                    {
                        Code = error.ErrorCode,
                        Description = error.ErrorDescription,
                        PossibleSolution = error.PossibleSolution,
                    })],
                    AdditionalInfo = entity.AdditionalInfo ?? [],
                })],
            }
            : null,
    };

    internal static FlowPreview? ToPreview(this FlowPreviewPayload? payload) =>
        ParseUri(payload?.PreviewUrl) is { } url
            ? new FlowPreview { Url = url, ExpiresAt = ParseTimestamp(payload!.ExpiresAt) }
            : null;

    internal static IReadOnlyList<FlowValidationError> ToValidationErrors(
        List<FlowValidationErrorPayload>? payloads) =>
        [.. (payloads ?? []).Select(payload => new FlowValidationError
        {
            Error = payload.Error ?? string.Empty,
            ErrorType = payload.ErrorType,
            Message = payload.Message,
            LineStart = payload.LineStart,
            LineEnd = payload.LineEnd,
            ColumnStart = payload.ColumnStart,
            ColumnEnd = payload.ColumnEnd,
            Paths = [.. (payload.Pointers ?? [])
                .Select(pointer => pointer.Path)
                .OfType<string>()],
        })];

    internal static FlowStatus ParseStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "DRAFT" => FlowStatus.Draft,
        "PUBLISHED" => FlowStatus.Published,
        "DEPRECATED" => FlowStatus.Deprecated,
        "BLOCKED" => FlowStatus.Blocked,
        "THROTTLED" => FlowStatus.Throttled,
        _ => FlowStatus.Unknown,
    };

    internal static FlowCategory ParseCategory(string? category) => category?.ToUpperInvariant() switch
    {
        "SIGN_UP" => FlowCategory.SignUp,
        "SIGN_IN" => FlowCategory.SignIn,
        "APPOINTMENT_BOOKING" => FlowCategory.AppointmentBooking,
        "LEAD_GENERATION" => FlowCategory.LeadGeneration,
        "CONTACT_US" => FlowCategory.ContactUs,
        "CUSTOMER_SUPPORT" => FlowCategory.CustomerSupport,
        "SURVEY" => FlowCategory.Survey,
        "OTHER" => FlowCategory.Other,
        _ => FlowCategory.Unknown,
    };

    internal static string ToWire(FlowCategory category) => category switch
    {
        FlowCategory.SignUp => "SIGN_UP",
        FlowCategory.SignIn => "SIGN_IN",
        FlowCategory.AppointmentBooking => "APPOINTMENT_BOOKING",
        FlowCategory.LeadGeneration => "LEAD_GENERATION",
        FlowCategory.ContactUs => "CONTACT_US",
        FlowCategory.CustomerSupport => "CUSTOMER_SUPPORT",
        FlowCategory.Survey => "SURVEY",
        FlowCategory.Other => "OTHER",
        // There is nothing sensible to send for a category that was read back as one this
        // library does not know, and Meta rejects the whole request over it.
        _ => throw new ArgumentException(
            $"{category} is not a category the Cloud API accepts. Pick one of the documented " +
            "categories rather than a value read back from an older Flow.",
            nameof(category)),
    };

    internal static MessagingAvailability ParseAvailability(string? value) => value?.ToUpperInvariant() switch
    {
        "AVAILABLE" => MessagingAvailability.Available,
        "LIMITED" => MessagingAvailability.Limited,
        "BLOCKED" => MessagingAvailability.Blocked,
        _ => MessagingAvailability.Unknown,
    };

    private static Uri? ParseUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}
