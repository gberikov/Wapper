namespace Wapper;

/// <summary>Base type for every failure this library raises.</summary>
public class WhatsAppException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public WhatsAppException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an underlying cause.</summary>
    public WhatsAppException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
