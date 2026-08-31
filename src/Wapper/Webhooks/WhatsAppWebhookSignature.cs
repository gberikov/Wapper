using System.Security.Cryptography;
using System.Text;

namespace Wapper.Webhooks;

/// <summary>
/// Checks that a webhook delivery really came from Meta.
/// </summary>
/// <remarks>
/// The endpoint is public, so without this anyone who learns the URL can post whatever they
/// like into the application. Meta signs the body with the app secret and sends the digest
/// in <see cref="HeaderName"/>.
/// </remarks>
public static class WhatsAppWebhookSignature
{
    /// <summary>The header carrying the signature.</summary>
    public const string HeaderName = "X-Hub-Signature-256";

    private const string Prefix = "sha256=";

    /// <summary>
    /// Whether the signature matches the body.
    /// </summary>
    /// <param name="body">
    /// The body exactly as it arrived. Not a re-serialized model: any change in whitespace or
    /// property order produces a different digest, and the check would fail on a genuine
    /// delivery.
    /// </param>
    /// <param name="signatureHeader">The value of <see cref="HeaderName"/>.</param>
    /// <param name="appSecret">The app secret from the Meta app dashboard.</param>
    public static bool IsValid(ReadOnlySpan<byte> body, string? signatureHeader, string appSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appSecret);

        if (string.IsNullOrEmpty(signatureHeader)
            || !signatureHeader.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var provided = signatureHeader.AsSpan(Prefix.Length);

        Span<byte> expected = stackalloc byte[SHA256.HashSizeInBytes];
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body, expected);

        Span<byte> actual = stackalloc byte[SHA256.HashSizeInBytes];
        if (!TryParseHex(provided, actual))
        {
            return false;
        }

        // Compared in fixed time: a byte-by-byte comparison that returns early leaks how much
        // of a forged digest was right, which is enough to build the rest of it.
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>
    /// Whether the token Meta sent on the verification handshake is the expected one.
    /// </summary>
    /// <remarks>
    /// Compared in fixed time for the same reason as the signature.
    /// </remarks>
    public static bool IsVerifyTokenValid(string? provided, string expected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expected);

        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expected));
    }

    private static bool TryParseHex(ReadOnlySpan<char> hex, Span<byte> destination)
    {
        if (hex.Length != destination.Length * 2)
        {
            return false;
        }

        for (var i = 0; i < destination.Length; i++)
        {
            if (!TryParseNibble(hex[i * 2], out var high) || !TryParseNibble(hex[(i * 2) + 1], out var low))
            {
                return false;
            }

            destination[i] = (byte)((high << 4) | low);
        }

        return true;
    }

    private static bool TryParseNibble(char character, out int value)
    {
        value = character switch
        {
            >= '0' and <= '9' => character - '0',
            >= 'a' and <= 'f' => character - 'a' + 10,
            >= 'A' and <= 'F' => character - 'A' + 10,
            _ => -1,
        };

        return value >= 0;
    }
}
