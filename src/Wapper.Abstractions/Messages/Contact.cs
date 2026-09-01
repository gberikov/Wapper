namespace Wapper.Messages;

/// <summary>A contact card, as WhatsApp shows it.</summary>
public sealed record Contact
{
    /// <summary>The name. The only part WhatsApp insists on.</summary>
    public required ContactName Name { get; init; }

    /// <summary>Phone numbers.</summary>
    public IReadOnlyList<ContactPhone> Phones { get; init; } = [];

    /// <summary>Email addresses.</summary>
    public IReadOnlyList<ContactEmail> Emails { get; init; } = [];

    /// <summary>Postal addresses.</summary>
    public IReadOnlyList<ContactAddress> Addresses { get; init; } = [];

    /// <summary>Web addresses.</summary>
    public IReadOnlyList<ContactUrl> Urls { get; init; } = [];

    /// <summary>Employer and job title.</summary>
    public ContactOrganisation? Organisation { get; init; }

    /// <summary>Birthday, formatted as <c>YYYY-MM-DD</c> on the wire.</summary>
    public DateOnly? Birthday { get; init; }

    /// <summary>
    /// The birthday exactly as it arrived, when it was not a full date — vCards allow
    /// partial forms such as <c>--05-21</c> that <see cref="Birthday"/> cannot hold.
    /// </summary>
    public string? RawBirthday { get; init; }
}

/// <summary>The parts of a contact's name.</summary>
/// <remarks>
/// WhatsApp requires <see cref="FormattedName"/> and at least one other part; a card with
/// only a formatted name is rejected.
/// </remarks>
public sealed record ContactName
{
    /// <summary>The full name as it should be displayed.</summary>
    public required string FormattedName { get; init; }

    /// <summary>Given name.</summary>
    public string? FirstName { get; init; }

    /// <summary>Family name.</summary>
    public string? LastName { get; init; }

    /// <summary>Middle name.</summary>
    public string? MiddleName { get; init; }

    /// <summary>Honorific before the name, such as <c>Dr</c>.</summary>
    public string? Prefix { get; init; }

    /// <summary>Qualification after the name, such as <c>PhD</c>.</summary>
    public string? Suffix { get; init; }
}

/// <summary>A phone number on a contact card.</summary>
public sealed record ContactPhone
{
    /// <summary>The number, as it should be displayed.</summary>
    public required string Phone { get; init; }

    /// <summary>What kind of number it is: <c>HOME</c>, <c>WORK</c>, <c>CELL</c> and so on.</summary>
    public string? Type { get; init; }

    /// <summary>
    /// The WhatsApp id, when the number is on WhatsApp. Setting it turns the card into one
    /// the recipient can message straight away.
    /// </summary>
    public string? WhatsAppId { get; init; }
}

/// <summary>An email address on a contact card.</summary>
public sealed record ContactEmail
{
    /// <summary>The address.</summary>
    public required string Email { get; init; }

    /// <summary>What kind of address it is, such as <c>HOME</c> or <c>WORK</c>.</summary>
    public string? Type { get; init; }
}

/// <summary>A web address on a contact card.</summary>
public sealed record ContactUrl
{
    /// <summary>The address.</summary>
    public required string Url { get; init; }

    /// <summary>What kind of address it is, such as <c>HOME</c> or <c>WORK</c>.</summary>
    public string? Type { get; init; }
}

/// <summary>A postal address on a contact card.</summary>
public sealed record ContactAddress
{
    /// <summary>Street and number.</summary>
    public string? Street { get; init; }

    /// <summary>City or town.</summary>
    public string? City { get; init; }

    /// <summary>State, province or region.</summary>
    public string? State { get; init; }

    /// <summary>Postal code.</summary>
    public string? Zip { get; init; }

    /// <summary>Country name.</summary>
    public string? Country { get; init; }

    /// <summary>Two-letter country code.</summary>
    public string? CountryCode { get; init; }

    /// <summary>What kind of address it is, such as <c>HOME</c> or <c>WORK</c>.</summary>
    public string? Type { get; init; }
}

/// <summary>Where a contact works.</summary>
public sealed record ContactOrganisation
{
    /// <summary>Employer.</summary>
    public string? Company { get; init; }

    /// <summary>Department within the employer.</summary>
    public string? Department { get; init; }

    /// <summary>Job title.</summary>
    public string? Title { get; init; }
}
