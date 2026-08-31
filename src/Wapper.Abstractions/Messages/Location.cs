namespace Wapper.Messages;

/// <summary>A point on the map, as WhatsApp shows it.</summary>
public sealed record Location
{
    /// <summary>Latitude in degrees.</summary>
    public required double Latitude { get; init; }

    /// <summary>Longitude in degrees.</summary>
    public required double Longitude { get; init; }

    /// <summary>Name of the place. Shown above the address.</summary>
    public string? Name { get; init; }

    /// <summary>Street address. Only shown when <see cref="Name"/> is set as well.</summary>
    public string? Address { get; init; }
}
