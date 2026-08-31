namespace Wapper.Sample;

/// <summary>Recipients are in international format without a leading plus: 15550001111.</summary>
internal sealed record SendText(string To, string Text);

internal sealed record SendOrderConfirmation(string To, string FirstName, string OrderNumber);

/// <summary>Up to three choices; a list message carries more.</summary>
internal sealed record SendChoice(string To, string Question, IReadOnlyList<string> Choices);

internal sealed record SendDocument(string To, string Path, string? Caption = null);
