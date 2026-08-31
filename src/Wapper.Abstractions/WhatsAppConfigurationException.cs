namespace Wapper;

/// <summary>
/// The client is not configured well enough to make a call: a missing token, an unknown
/// tenant, a malformed Graph API version.
/// </summary>
/// <remarks>
/// Always a programming or deployment fault, never something to retry.
/// </remarks>
public sealed class WhatsAppConfigurationException : WhatsAppException
{
    /// <summary>Creates the exception with a message.</summary>
    public WhatsAppConfigurationException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an underlying cause.</summary>
    public WhatsAppConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
