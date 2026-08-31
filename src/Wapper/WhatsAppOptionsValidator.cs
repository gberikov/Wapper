using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Wapper;

/// <summary>
/// Fails startup on configuration that could only fail later, in production, as an
/// unhelpful HTTP error.
/// </summary>
internal sealed partial class WhatsAppOptionsValidator : IValidateOptions<WhatsAppOptions>
{
    public ValidateOptionsResult Validate(string? name, WhatsAppOptions options)
    {
        var failures = new List<string>();
        var tenant = string.IsNullOrEmpty(name) ? "the default tenant" : $"tenant '{name}'";

        if (!GraphApiVersionPattern().IsMatch(options.GraphApiVersion))
        {
            failures.Add(
                $"GraphApiVersion of {tenant} is '{options.GraphApiVersion}'; expected a Graph API " +
                "version such as 'v26.0'.");
        }

        if (!options.BaseAddress.IsAbsoluteUri)
        {
            failures.Add($"BaseAddress of {tenant} must be an absolute URI.");
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            failures.Add($"Timeout of {tenant} must be greater than zero.");
        }

        // AccessToken and PhoneNumberId are deliberately not required here. A multi-tenant
        // host supplies them from its own store through IWhatsAppCredentialsProvider, and
        // demanding them in configuration would make that arrangement impossible.
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    [GeneratedRegex(@"^v\d+\.\d+$")]
    private static partial Regex GraphApiVersionPattern();
}
