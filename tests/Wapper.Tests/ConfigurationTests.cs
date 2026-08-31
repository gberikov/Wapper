using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wapper.Tests.Fakes;

namespace Wapper.Tests;

public class WhatsAppOptionsValidationTests
{
    [Theory]
    [InlineData("26.0")]
    [InlineData("v26")]
    [InlineData("latest")]
    [InlineData("")]
    public void Malformed_graph_api_version_is_rejected(string version)
    {
        var result = Validate(options => options.GraphApiVersion = version);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("GraphApiVersion", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("v23.0")]
    [InlineData("v26.0")]
    [InlineData("v100.12")]
    public void Well_formed_graph_api_version_is_accepted(string version)
    {
        Assert.True(Validate(options => options.GraphApiVersion = version).Succeeded);
    }

    [Fact]
    public void Non_positive_timeout_is_rejected()
    {
        Assert.True(Validate(options => options.Timeout = TimeSpan.Zero).Failed);
    }

    [Fact]
    public void Missing_credentials_are_not_a_configuration_failure()
    {
        // A multi-tenant host resolves tokens from its own store, so demanding them in
        // configuration would make that arrangement impossible to express.
        Assert.True(Validate(options =>
        {
            options.AccessToken = null;
            options.PhoneNumberId = null;
        }).Succeeded);
    }

    [Fact]
    public void Failure_message_names_the_tenant()
    {
        var result = new WhatsAppOptionsValidator()
            .Validate("acme", new WhatsAppOptions { GraphApiVersion = "nope" });

        Assert.Contains(result.Failures!, f => f.Contains("acme", StringComparison.Ordinal));
    }

    private static ValidateOptionsResult Validate(Action<WhatsAppOptions> configure)
    {
        var options = new WhatsAppOptions();
        configure(options);

        return new WhatsAppOptionsValidator().Validate(WhatsAppTenant.Default, options);
    }
}

public class OptionsCredentialsProviderTests
{
    [Fact]
    public async Task Credentials_come_from_the_options_of_the_named_tenant()
    {
        var services = new ServiceCollection();
        services.AddWhatsApp(options =>
        {
            options.AccessToken = "default-token";
            options.PhoneNumberId = "111";
        });
        services.AddWhatsApp("acme", options =>
        {
            options.AccessToken = "acme-token";
            options.PhoneNumberId = "222";
            options.WhatsAppBusinessAccountId = "waba-222";
        });

        var provider = services.BuildServiceProvider()
            .GetRequiredService<IWhatsAppCredentialsProvider>();

        var @default = await provider.GetCredentialsAsync(
            WhatsAppTenant.Default,
            TestContext.Current.CancellationToken);
        var acme = await provider.GetCredentialsAsync("acme", TestContext.Current.CancellationToken);

        Assert.Equal("default-token", @default.AccessToken);
        Assert.Equal("111", @default.PhoneNumberId);
        Assert.Null(@default.WhatsAppBusinessAccountId);

        Assert.Equal("acme-token", acme.AccessToken);
        Assert.Equal("222", acme.PhoneNumberId);
        Assert.Equal("waba-222", acme.WhatsAppBusinessAccountId);
    }

    [Theory]
    [InlineData(null, "111", "access token")]
    [InlineData("token", null, "phone number id")]
    public async Task Missing_credentials_fail_with_an_actionable_message(
        string? token,
        string? phoneNumberId,
        string expected)
    {
        var provider = new OptionsCredentialsProvider(
            new StaticOptionsMonitor<WhatsAppOptions>(new WhatsAppOptions
            {
                AccessToken = token,
                PhoneNumberId = phoneNumberId,
            }));

        var exception = await Assert.ThrowsAsync<WhatsAppConfigurationException>(async () =>
            await provider.GetCredentialsAsync("acme", TestContext.Current.CancellationToken));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        // The message has to say which tenant, or a multi-tenant host cannot act on it.
        Assert.Contains("acme", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IWhatsAppCredentialsProvider), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_custom_provider_replaces_the_configuration_one()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWhatsAppCredentialsProvider>(
            new StubCredentialsProvider(new WhatsAppCredentials
            {
                AccessToken = "from-database",
                PhoneNumberId = "999",
            }));
        services.AddWhatsApp();

        var provider = services.BuildServiceProvider()
            .GetRequiredService<IWhatsAppCredentialsProvider>();

        Assert.IsType<StubCredentialsProvider>(provider);
    }
}

public class ConfigurationBindingTests
{
    [Fact]
    public void Options_bind_from_a_configuration_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WhatsApp:AccessToken"] = "bound-token",
                ["WhatsApp:PhoneNumberId"] = "333",
                ["WhatsApp:GraphApiVersion"] = "v25.0",
                ["WhatsApp:BaseAddress"] = "https://proxy.internal/graph/",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddWhatsApp(configuration.GetSection(WhatsAppOptions.SectionName));

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<WhatsAppOptions>>()
            .Get(WhatsAppTenant.Default);

        Assert.Equal("bound-token", options.AccessToken);
        Assert.Equal("333", options.PhoneNumberId);
        Assert.Equal("v25.0", options.GraphApiVersion);
        Assert.Equal(new Uri("https://proxy.internal/graph/"), options.BaseAddress);
    }

    [Fact]
    public void Code_configuration_wins_over_the_bound_values()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WhatsApp:GraphApiVersion"] = "v23.0",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddWhatsApp(
            configuration.GetSection(WhatsAppOptions.SectionName),
            options => options.GraphApiVersion = "v26.0");

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<WhatsAppOptions>>()
            .Get(WhatsAppTenant.Default);

        Assert.Equal("v26.0", options.GraphApiVersion);
    }
}
