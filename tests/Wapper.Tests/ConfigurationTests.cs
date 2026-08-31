using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wapper.RateLimiting;
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
    public void A_base_address_that_is_not_https_is_rejected()
    {
        // The access token is a bearer token: it is worth exactly as much to whoever reads it
        // off the wire.
        var result = Validate(options => options.BaseAddress = new Uri("http://graph.facebook.com/"));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("https", StringComparison.Ordinal));
    }

    [Fact]
    public void A_loopback_base_address_may_be_plaintext()
    {
        // A test server or a local proxy should not have to hold a certificate, and nothing
        // leaves the machine.
        Assert.True(Validate(options => options.BaseAddress = new Uri("http://localhost:8080/")).Succeeded);
    }

    [Theory]
    [MemberData(nameof(BrokenRateLimits))]
    public void A_rate_limit_that_could_never_pace_anything_is_rejected(
        Action<WhatsAppRateLimitOptions> configure,
        string expected)
    {
        // Every one of these is a value the limiter divides by or paces against. Left wrong,
        // they surface in production as messages that never send, or as a block from Meta.
        var result = Validate(options => configure(options.RateLimits));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(expected, StringComparison.Ordinal));
    }

    public static TheoryData<Action<WhatsAppRateLimitOptions>, string> BrokenRateLimits() => new()
    {
        { limits => limits.MessagesPerSecond = 0, "MessagesPerSecond" },
        { limits => limits.PairInterval = TimeSpan.Zero, "PairInterval" },
        { limits => limits.PairBurst = 0, "PairBurst" },
        { limits => limits.BusinessAccountRequestsPerHour = 0, "BusinessAccountRequestsPerHour" },
        { limits => limits.MaxWait = TimeSpan.FromSeconds(-1), "MaxWait" },
        { limits => limits.MaxRetries = -1, "MaxRetries" },
        { limits => limits.UsagePercentThreshold = 0, "UsagePercentThreshold" },
        { limits => limits.UsagePercentThreshold = 101, "UsagePercentThreshold" },
    };

    [Fact]
    public void The_defaults_pass_their_own_validation()
    {
        Assert.True(Validate(_ => { }).Succeeded);
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

/// <summary>
/// What <c>AddWhatsApp()</c> does when nobody hands it a section: it finds the conventional
/// one itself, so a single-number application registers the client in one call with no
/// arguments at all.
/// </summary>
public class ConventionalConfigurationTests
{
    [Fact]
    public void No_arguments_reads_the_WhatsApp_section()
    {
        var options = Resolve(
            new Dictionary<string, string?>
            {
                ["WhatsApp:AccessToken"] = "the-token",
                ["WhatsApp:PhoneNumberId"] = "106540352242922",
                ["WhatsApp:GraphApiVersion"] = "v27.0",
            },
            services => services.AddWhatsApp());

        var @default = options.Get(WhatsAppTenant.Default);
        Assert.Equal("the-token", @default.AccessToken);
        Assert.Equal("106540352242922", @default.PhoneNumberId);
        Assert.Equal("v27.0", @default.GraphApiVersion);
    }

    [Fact]
    public void A_tenant_is_bound_the_first_time_it_is_asked_for()
    {
        // Nothing enumerated the tenants, because nothing was given the section to enumerate.
        // The name arrives with the request for the options, which is enough.
        var options = Resolve(
            new Dictionary<string, string?>
            {
                ["WhatsApp:WhatsAppBusinessAccountId"] = "shared-waba",
                ["WhatsApp:RateLimits:MessagesPerSecond"] = "80",
                ["WhatsApp:Tenants:acme:AccessToken"] = "acme-token",
                ["WhatsApp:Tenants:acme:PhoneNumberId"] = "111",
                ["WhatsApp:Tenants:globex:AccessToken"] = "globex-token",
                ["WhatsApp:Tenants:globex:PhoneNumberId"] = "222",
                ["WhatsApp:Tenants:globex:RateLimits:MessagesPerSecond"] = "1000",
            },
            services => services.AddWhatsApp());

        var acme = options.Get("acme");
        Assert.Equal("acme-token", acme.AccessToken);
        // Inherited from the section around it, exactly as when the section is passed in.
        Assert.Equal("shared-waba", acme.WhatsAppBusinessAccountId);
        Assert.Equal(80, acme.RateLimits.MessagesPerSecond);

        Assert.Equal(1000, options.Get("globex").RateLimits.MessagesPerSecond);
    }

    [Fact]
    public void Code_still_wins_over_the_section_it_found()
    {
        var options = Resolve(
            new Dictionary<string, string?>
            {
                ["WhatsApp:AccessToken"] = "from-configuration",
                ["WhatsApp:GraphApiVersion"] = "v23.0",
            },
            services => services.AddWhatsApp(o => o.GraphApiVersion = "v26.0"));

        var @default = options.Get(WhatsAppTenant.Default);
        // The delegate runs after the binding, so it pins one value and leaves the rest.
        Assert.Equal("v26.0", @default.GraphApiVersion);
        Assert.Equal("from-configuration", @default.AccessToken);
    }

    [Fact]
    public void Configuring_entirely_in_code_needs_no_configuration_at_all()
    {
        // A console application or a test has no IConfiguration, and asking for one would
        // turn that into a resolve-time failure for a registration that never wanted it.
        var services = new ServiceCollection();
        services.AddWhatsApp(o =>
        {
            o.AccessToken = "in-code";
            o.PhoneNumberId = "111";
        });

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<WhatsAppOptions>>()
            .Get(WhatsAppTenant.Default);

        Assert.Equal("in-code", options.AccessToken);
    }

    [Fact]
    public void A_section_that_is_not_there_leaves_the_defaults_alone()
    {
        var options = Resolve(
            new Dictionary<string, string?> { ["Something:Else"] = "x" },
            services => services.AddWhatsApp());

        Assert.Equal("v26.0", options.Get(WhatsAppTenant.Default).GraphApiVersion);
        Assert.Null(options.Get(WhatsAppTenant.Default).AccessToken);
    }

    [Fact]
    public void Registering_several_tenants_in_code_binds_each_from_its_own_entry()
    {
        var options = Resolve(
            new Dictionary<string, string?>
            {
                ["WhatsApp:Tenants:acme:AccessToken"] = "acme-token",
                ["WhatsApp:Tenants:acme:PhoneNumberId"] = "111",
                ["WhatsApp:Tenants:globex:AccessToken"] = "globex-token",
                ["WhatsApp:Tenants:globex:PhoneNumberId"] = "222",
            },
            services =>
            {
                // Two calls, so the convention binding is registered twice and has to
                // deduplicate rather than bind everything twice over.
                services.AddWhatsApp("acme");
                services.AddWhatsApp("globex");
            });

        Assert.Equal("acme-token", options.Get("acme").AccessToken);
        Assert.Equal("globex-token", options.Get("globex").AccessToken);

        // Bound once, so the defaults are not repeated. Configuration adds to this list
        // rather than replacing it, which makes a double bind visible.
        Assert.Equal(4, options.Get("acme").MediaDownloadHosts.Count);
    }

    [Fact]
    public void A_section_of_another_name_is_read_instead_of_the_conventional_one()
    {
        // The other half of the bargain: naming a section means read that one, so a stray
        // "WhatsApp" section left in appsettings cannot quietly supply a token or an API
        // version to an application that deliberately keeps its settings somewhere else.
        var options = Resolve(
            new Dictionary<string, string?>
            {
                ["WhatsApp:AccessToken"] = "the-wrong-token",
                ["WhatsApp:GraphApiVersion"] = "v23.0",

                ["Messaging:WhatsAppCloud:AccessToken"] = "the-right-token",
                ["Messaging:WhatsAppCloud:PhoneNumberId"] = "106540352242922",
                ["Messaging:WhatsAppCloud:Tenants:acme:PhoneNumberId"] = "111",
            },
            services => services.AddWhatsApp(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AccessToken"] = "the-right-token",
                        ["PhoneNumberId"] = "106540352242922",
                        ["Tenants:acme:PhoneNumberId"] = "111",
                    })
                    .Build()));

        var @default = options.Get(WhatsAppTenant.Default);
        Assert.Equal("the-right-token", @default.AccessToken);
        Assert.Equal("v26.0", @default.GraphApiVersion);

        // Tenants hang off whichever section was named, not off "WhatsApp".
        var acme = options.Get("acme");
        Assert.Equal("111", acme.PhoneNumberId);
        Assert.Equal("the-right-token", acme.AccessToken);
    }

    private static IOptionsMonitor<WhatsAppOptions> Resolve(
        Dictionary<string, string?> settings,
        Action<IServiceCollection> register)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        // What a host registers, and the only thing the convention has to go on.
        services.AddSingleton<IConfiguration>(configuration);
        register(services);

        return services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<WhatsAppOptions>>();
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
    public void One_phone_number_needs_nothing_but_the_section()
    {
        // The single-tenant case has to stay this small: no tenant to name, nothing to
        // enumerate, one call.
        var options = Bind(new Dictionary<string, string?>
        {
            ["WhatsApp:AccessToken"] = "the-token",
            ["WhatsApp:PhoneNumberId"] = "106540352242922",
        });

        var @default = options.Get(WhatsAppTenant.Default);
        Assert.Equal("the-token", @default.AccessToken);
        Assert.Equal("106540352242922", @default.PhoneNumberId);
    }

    [Fact]
    public void Every_entry_under_Tenants_is_registered_under_its_own_name()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["WhatsApp:Tenants:acme:AccessToken"] = "acme-token",
            ["WhatsApp:Tenants:acme:PhoneNumberId"] = "111",
            ["WhatsApp:Tenants:globex:AccessToken"] = "globex-token",
            ["WhatsApp:Tenants:globex:PhoneNumberId"] = "222",
        });

        Assert.Equal("acme-token", options.Get("acme").AccessToken);
        Assert.Equal("111", options.Get("acme").PhoneNumberId);
        Assert.Equal("globex-token", options.Get("globex").AccessToken);
        Assert.Equal("222", options.Get("globex").PhoneNumberId);
    }

    [Fact]
    public void A_tenant_inherits_what_is_set_alongside_it()
    {
        // Otherwise the app secret and the API version would have to be repeated per tenant,
        // and the copy that drifts is the one nobody notices.
        var options = Bind(new Dictionary<string, string?>
        {
            ["WhatsApp:GraphApiVersion"] = "v27.0",
            ["WhatsApp:AppSecret"] = "shared-secret",
            ["WhatsApp:RateLimits:MessagesPerSecond"] = "1000",
            ["WhatsApp:Tenants:acme:AccessToken"] = "acme-token",
            ["WhatsApp:Tenants:acme:PhoneNumberId"] = "111",
        });

        var acme = options.Get("acme");
        Assert.Equal("v27.0", acme.GraphApiVersion);
        Assert.Equal("shared-secret", acme.AppSecret);
        Assert.Equal(1000, acme.RateLimits.MessagesPerSecond);
    }

    [Fact]
    public void What_a_tenant_sets_itself_wins_over_what_it_inherits()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["WhatsApp:GraphApiVersion"] = "v26.0",
            ["WhatsApp:RateLimits:MessagesPerSecond"] = "80",
            ["WhatsApp:Tenants:acme:AccessToken"] = "acme-token",
            ["WhatsApp:Tenants:acme:PhoneNumberId"] = "111",
            ["WhatsApp:Tenants:globex:AccessToken"] = "globex-token",
            ["WhatsApp:Tenants:globex:PhoneNumberId"] = "222",
            // This number has been upgraded and the other has not.
            ["WhatsApp:Tenants:globex:RateLimits:MessagesPerSecond"] = "1000",
            ["WhatsApp:Tenants:globex:GraphApiVersion"] = "v27.0",
        });

        Assert.Equal(80, options.Get("acme").RateLimits.MessagesPerSecond);
        Assert.Equal("v26.0", options.Get("acme").GraphApiVersion);

        Assert.Equal(1000, options.Get("globex").RateLimits.MessagesPerSecond);
        Assert.Equal("v27.0", options.Get("globex").GraphApiVersion);
    }

    [Fact]
    public void The_default_tenant_keeps_the_webhook_secrets_and_no_credentials()
    {
        // One webhook endpoint serves every number of one Meta app, and it reads the app
        // secret from the default tenant. Leaving that tenant without a token is the point:
        // a forgotten For(...) then fails loudly instead of sending as somebody else.
        var options = Bind(new Dictionary<string, string?>
        {
            ["WhatsApp:AppSecret"] = "shared-secret",
            ["WhatsApp:WebhookVerifyToken"] = "shared-verify",
            ["WhatsApp:Tenants:acme:AccessToken"] = "acme-token",
            ["WhatsApp:Tenants:acme:PhoneNumberId"] = "111",
        });

        var @default = options.Get(WhatsAppTenant.Default);
        Assert.Equal("shared-secret", @default.AppSecret);
        Assert.Equal("shared-verify", @default.WebhookVerifyToken);
        Assert.Null(@default.AccessToken);
    }

    [Fact]
    public void A_single_tenant_may_still_be_written_as_one_entry_under_Tenants()
    {
        // For anyone who would rather have one shape whatever the number of tenants. It costs
        // naming the tenant on every call, which is why it is not what the quickstart shows.
        var options = Bind(new Dictionary<string, string?>
        {
            ["WhatsApp:Tenants:main:AccessToken"] = "the-token",
            ["WhatsApp:Tenants:main:PhoneNumberId"] = "106540352242922",
        });

        Assert.Equal("the-token", options.Get("main").AccessToken);
    }

    [Fact]
    public void Code_configuration_wins_over_every_tenant_it_registered()
    {
        var services = new ServiceCollection();
        services.AddWhatsApp(
            Configuration(new Dictionary<string, string?>
            {
                ["WhatsApp:GraphApiVersion"] = "v23.0",
                ["WhatsApp:Tenants:acme:AccessToken"] = "acme-token",
                ["WhatsApp:Tenants:acme:PhoneNumberId"] = "111",
                ["WhatsApp:Tenants:acme:GraphApiVersion"] = "v24.0",
            }).GetSection(WhatsAppOptions.SectionName),
            options => options.GraphApiVersion = "v26.0");

        var monitor = services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<WhatsAppOptions>>();

        Assert.Equal("v26.0", monitor.Get(WhatsAppTenant.Default).GraphApiVersion);
        Assert.Equal("v26.0", monitor.Get("acme").GraphApiVersion);
    }

    [Fact]
    public async Task Credentials_are_resolved_per_tenant_from_one_section()
    {
        var services = new ServiceCollection();
        services.AddWhatsApp(
            Configuration(new Dictionary<string, string?>
            {
                ["WhatsApp:WhatsAppBusinessAccountId"] = "shared-waba",
                ["WhatsApp:Tenants:acme:AccessToken"] = "acme-token",
                ["WhatsApp:Tenants:acme:PhoneNumberId"] = "111",
                ["WhatsApp:Tenants:globex:AccessToken"] = "globex-token",
                ["WhatsApp:Tenants:globex:PhoneNumberId"] = "222",
            }).GetSection(WhatsAppOptions.SectionName));

        var provider = services.BuildServiceProvider().GetRequiredService<IWhatsAppCredentialsProvider>();

        var acme = await provider.GetCredentialsAsync("acme", TestContext.Current.CancellationToken);
        var globex = await provider.GetCredentialsAsync("globex", TestContext.Current.CancellationToken);

        Assert.Equal("acme-token", acme.AccessToken);
        Assert.Equal("111", acme.PhoneNumberId);
        // Inherited, because both numbers hang off the same account.
        Assert.Equal("shared-waba", acme.WhatsAppBusinessAccountId);

        Assert.Equal("globex-token", globex.AccessToken);
        Assert.Equal("222", globex.PhoneNumberId);
    }

    [Fact]
    public async Task Sending_through_a_tenant_that_configuration_never_named_says_so()
    {
        var services = new ServiceCollection();
        services.AddWhatsApp(
            Configuration(new Dictionary<string, string?>
            {
                ["WhatsApp:Tenants:acme:AccessToken"] = "acme-token",
                ["WhatsApp:Tenants:acme:PhoneNumberId"] = "111",
            }).GetSection(WhatsAppOptions.SectionName));

        var provider = services.BuildServiceProvider().GetRequiredService<IWhatsAppCredentialsProvider>();

        var exception = await Assert.ThrowsAsync<WhatsAppConfigurationException>(async () =>
            await provider.GetCredentialsAsync("typo", TestContext.Current.CancellationToken));

        // Names the tenant, so a misspelled key in appsettings is one line to find.
        Assert.Contains("typo", exception.Message, StringComparison.Ordinal);
    }

    private static IOptionsMonitor<WhatsAppOptions> Bind(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddWhatsApp(Configuration(settings).GetSection(WhatsAppOptions.SectionName));

        return services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<WhatsAppOptions>>();
    }

    private static IConfigurationRoot Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

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
