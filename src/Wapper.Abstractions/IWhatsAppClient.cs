using Wapper.BusinessProfiles;
using Wapper.Media;
using Wapper.Messages;
using Wapper.PhoneNumbers;
using Wapper.Templates;

namespace Wapper;

/// <summary>Everything the Cloud API offers, for one tenant.</summary>
public interface IWhatsAppTenantClient
{
    /// <summary>Which tenant this client acts as.</summary>
    string Tenant { get; }

    /// <summary>Sending messages, and acknowledging the ones that arrive.</summary>
    IMessagesApi Messages { get; }

    /// <summary>Uploading, locating, downloading and deleting media.</summary>
    IMediaApi Media { get; }

    /// <summary>Creating, reading, editing and deleting message templates.</summary>
    ITemplatesApi Templates { get; }

    /// <summary>Reading the business phone numbers of the account.</summary>
    IPhoneNumbersApi PhoneNumbers { get; }

    /// <summary>Reading and editing the profile shown behind the phone number.</summary>
    IBusinessProfileApi BusinessProfile { get; }
}

/// <summary>
/// Entry point to the Cloud API. Acts as the default tenant, and hands out clients for the
/// others.
/// </summary>
/// <remarks>
/// An application with one business phone number can ignore tenants entirely and call
/// straight through this. A host serving many accounts calls <see cref="For"/>.
/// </remarks>
public interface IWhatsAppClient : IWhatsAppTenantClient
{
    /// <summary>A client acting as the named tenant.</summary>
    /// <param name="tenant">
    /// The name the tenant was registered under, or that a custom
    /// <see cref="IWhatsAppCredentialsProvider"/> understands.
    /// </param>
    IWhatsAppTenantClient For(string tenant);
}
