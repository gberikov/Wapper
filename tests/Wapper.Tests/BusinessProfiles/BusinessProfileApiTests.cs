using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Wapper.BusinessProfiles;
using Wapper.Internal;
using Wapper.RateLimiting;
using Wapper.Tests.Fakes;

namespace Wapper.Tests.BusinessProfiles;

public class BusinessProfileApiTests
{
    private const string Ok = """{"success":true}""";

    /// <summary>A filled-in profile, wrapped the way Meta wraps it.</summary>
    private const string Profile = """
        {
          "data": [{
            "about": "We sell butterflies.",
            "address": "101 Butterfly Ln., Butterfly, Ohio",
            "description": "Butterflies, and the things butterflies need.",
            "email": "hello@butterflies.example",
            "messaging_product": "whatsapp",
            "profile_picture_url": "https://pps.whatsapp.net/v/t61/butterfly.jpg",
            "websites": ["https://www.butterflies.example/"],
            "vertical": "RETAIL"
          }]
        }
        """;

    private static readonly WhatsAppCredentials Credentials = new()
    {
        AccessToken = "token-abc",
        PhoneNumberId = "106540352242922",
        WhatsAppBusinessAccountId = "102290129340398",
        AppId = "1234567890",
    };

    [Fact]
    public async Task Reading_the_profile_asks_for_every_field()
    {
        var (profiles, handler) = Create(Profile);

        await profiles.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Without a field list Graph answers with the messaging product and nothing else.
        Assert.Equal(
            "https://graph.facebook.com/v26.0/106540352242922/whatsapp_business_profile" +
            "?fields=about,address,description,email,profile_picture_url,websites,vertical",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task The_profile_is_read_out_of_the_one_element_array_it_arrives_in()
    {
        var (profiles, _) = Create(Profile);

        var profile = await profiles.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        // A phone number has exactly one profile, and Meta still returns a collection.
        Assert.Equal("We sell butterflies.", profile.About);
        Assert.Equal("101 Butterfly Ln., Butterfly, Ohio", profile.Address);
        Assert.Equal("Butterflies, and the things butterflies need.", profile.Description);
        Assert.Equal("hello@butterflies.example", profile.Email);
        Assert.Equal(BusinessVertical.Retail, profile.Vertical);
        Assert.Equal("https://pps.whatsapp.net/v/t61/butterfly.jpg", profile.PictureUrl);
        Assert.Equal(["https://www.butterflies.example/"], profile.Websites!);
    }

    [Fact]
    public async Task A_profile_that_has_never_been_filled_in_reads_as_empty()
    {
        var (profiles, _) = Create("""{"data":[]}""");

        var profile = await profiles.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        // An empty array, not an error: nobody has set anything yet.
        Assert.Null(profile.About);
        Assert.Null(profile.Vertical);
    }

    [Fact]
    public async Task A_category_this_library_does_not_know_is_kept_as_written()
    {
        var (profiles, _) = Create("""{"data":[{"vertical":"UNDEFINED"}]}""");

        var profile = await profiles.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(BusinessVertical.Unknown, profile.Vertical);
        Assert.Equal("UNDEFINED", profile.RawVertical);
    }

    [Fact]
    public async Task An_update_sends_only_what_was_set()
    {
        var (profiles, handler) = Create(Ok);

        await profiles.UpdateAsync(
            new BusinessProfile { About = "Now with more butterflies" },
            cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://graph.facebook.com/v26.0/106540352242922/whatsapp_business_profile",
            request.RequestUri!.AbsoluteUri);

        var body = JsonDocument.Parse(Assert.Single(handler.Bodies)!).RootElement;
        Assert.Equal("whatsapp", body.GetProperty("messaging_product").GetString());
        Assert.Equal("Now with more butterflies", body.GetProperty("about").GetString());
        // The Cloud API merges rather than replaces, so a field left unset has to stay off the
        // wire — sending it as null would be an edit.
        Assert.False(body.TryGetProperty("address", out _));
        Assert.False(body.TryGetProperty("vertical", out _));
    }

    [Fact]
    public async Task Clearing_the_category_sends_the_empty_string_Meta_asks_for()
    {
        var (profiles, handler) = Create(Ok);

        await profiles.UpdateAsync(
            new BusinessProfile { Vertical = BusinessVertical.Unknown },
            cancellationToken: TestContext.Current.CancellationToken);

        var body = JsonDocument.Parse(Assert.Single(handler.Bodies)!).RootElement;
        Assert.Equal(string.Empty, body.GetProperty("vertical").GetString());
    }

    [Fact]
    public async Task A_category_goes_up_under_Metas_name_for_it()
    {
        var (profiles, handler) = Create(Ok);

        await profiles.UpdateAsync(
            new BusinessProfile { Vertical = BusinessVertical.OverTheCounterDrugs },
            cancellationToken: TestContext.Current.CancellationToken);

        var body = JsonDocument.Parse(Assert.Single(handler.Bodies)!).RootElement;
        Assert.Equal("OTC_DRUGS", body.GetProperty("vertical").GetString());
    }

    [Fact]
    public async Task A_field_longer_than_Meta_accepts_never_reaches_it()
    {
        var (profiles, handler) = Create(Ok);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            profiles.UpdateAsync(
                new BusinessProfile { About = new string('x', 140) },
                cancellationToken: TestContext.Current.CancellationToken));

        // Meta answers every one of these with a bare code 100 that does not name the field.
        Assert.Contains("About", exception.Message, StringComparison.Ordinal);
        Assert.Contains("139", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Something_that_is_not_an_email_address_never_reaches_Meta()
    {
        var (profiles, handler) = Create(Ok);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            profiles.UpdateAsync(
                new BusinessProfile { Email = "butterflies at example dot com" },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_third_website_never_reaches_Meta()
    {
        var (profiles, handler) = Create(Ok);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            profiles.UpdateAsync(
                new BusinessProfile
                {
                    Websites = ["https://a.example", "https://b.example", "https://c.example"],
                },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_website_without_a_scheme_never_reaches_Meta()
    {
        var (profiles, handler) = Create(Ok);

        // Stored as given, and shown to the recipient as unclickable text.
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            profiles.UpdateAsync(
                new BusinessProfile { Websites = ["www.butterflies.example"] },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("http://", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Setting_a_picture_uploads_it_and_then_applies_the_handle()
    {
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.OK, """{"id":"upload:MTphdHRhY2htZW50"}"""),
            (HttpStatusCode.OK, """{"h":"4:cHJvZmlsZQ=="}"""),
            (HttpStatusCode.OK, Ok));

        await CreateWith(handler, Credentials).SetPictureAsync(
            new MemoryStream([1, 2, 3, 4, 5]),
            "image/png",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, handler.Requests.Count);

        // The session is opened against the Meta app, not against anything WhatsApp-shaped,
        // and has to declare the length up front.
        Assert.Equal(
            "https://graph.facebook.com/v26.0/1234567890/uploads" +
            "?file_name=profile&file_length=5&file_type=image%2Fpng",
            handler.Requests[0].RequestUri!.AbsoluteUri);

        // The session id already carries its own "upload:" prefix.
        Assert.Equal(
            "https://graph.facebook.com/v26.0/upload:MTphdHRhY2htZW50",
            handler.Requests[1].RequestUri!.AbsoluteUri);

        var handle = JsonDocument.Parse(handler.Bodies[2]!).RootElement
            .GetProperty("profile_picture_handle").GetString();
        Assert.Equal("4:cHJvZmlsZQ==", handle);
    }

    [Fact]
    public async Task The_upload_presents_the_token_under_the_scheme_that_endpoint_wants()
    {
        var handler = StubHttpMessageHandler.Sequence(
            (HttpStatusCode.OK, """{"id":"upload:MTphdHRhY2htZW50"}"""),
            (HttpStatusCode.OK, """{"h":"4:cHJvZmlsZQ=="}"""),
            (HttpStatusCode.OK, Ok));

        await CreateWith(handler, Credentials).SetPictureAsync(
            new MemoryStream([1, 2, 3]),
            "image/jpeg",
            cancellationToken: TestContext.Current.CancellationToken);

        // Bearer everywhere else; the resumable upload refuses it and wants OAuth, plus a
        // file_offset saying where to resume from.
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization!.Scheme);
        Assert.Equal("OAuth", handler.Requests[1].Headers.Authorization!.Scheme);
        Assert.Equal("token-abc", handler.Requests[1].Headers.Authorization!.Parameter);
        Assert.Equal("0", Assert.Single(handler.Requests[1].Headers.GetValues("file_offset")));
    }

    [Fact]
    public async Task Setting_a_picture_without_an_app_id_says_which_setting_is_missing()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, Ok);
        var profiles = CreateWith(handler, Credentials with { AppId = null });

        var exception = await Assert.ThrowsAsync<WhatsAppConfigurationException>(() =>
            profiles.SetPictureAsync(
                new MemoryStream([1]),
                "image/png",
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("AppId", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Reading_another_numbers_profile_addresses_that_one()
    {
        var (profiles, handler) = Create(Profile);

        await profiles.GetAsync("999888777", TestContext.Current.CancellationToken);

        Assert.StartsWith(
            "https://graph.facebook.com/v26.0/999888777/whatsapp_business_profile",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_category_this_library_does_not_know_survives_a_round_trip_untouched()
    {
        const string Unrecognised = """
            {"data":[{"about":"We sell butterflies.","vertical":"TELECOM"}]}
            """;
        var (profiles, _) = Create(Unrecognised);

        var profile = await profiles.GetAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(BusinessVertical.Unknown, profile.Vertical);
        Assert.Equal("TELECOM", profile.RawVertical);

        // Writing the profile back with an unrelated edit must not clear the category: the
        // empty string Unknown maps to is the documented clear, and this Unknown is merely a
        // vertical this library has not been taught.
        var (editor, handler) = Create(Ok);
        await editor.UpdateAsync(
            profile with { About = "New about" },
            cancellationToken: TestContext.Current.CancellationToken);

        var body = JsonDocument.Parse(Assert.Single(handler.Bodies)!).RootElement;
        Assert.Equal("New about", body.GetProperty("about").GetString());
        Assert.False(body.TryGetProperty("vertical", out _));
    }

    [Fact]
    public async Task An_explicit_unknown_still_clears_the_category()
    {
        var (profiles, handler) = Create(Ok);

        // Unknown with no raw value behind it is a hand-built request, and the empty string
        // is the documented way to clear.
        await profiles.UpdateAsync(
            new BusinessProfile { Vertical = BusinessVertical.Unknown },
            cancellationToken: TestContext.Current.CancellationToken);

        var body = JsonDocument.Parse(Assert.Single(handler.Bodies)!).RootElement;
        Assert.Equal(string.Empty, body.GetProperty("vertical").GetString());
    }

    private static (IBusinessProfileApi Profiles, StubHttpMessageHandler Handler) Create(string response)
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, response);
        return (CreateWith(handler, Credentials), handler);
    }

    private static IBusinessProfileApi CreateWith(
        StubHttpMessageHandler handler,
        WhatsAppCredentials credentials)
    {
        var time = new FakeTimeProvider();

        return new BusinessProfileApi(
            new GraphApiClient(
                new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
                new StubCredentialsProvider(credentials),
                new InMemoryRateLimiter(time),
                new StaticOptionsMonitor<WhatsAppOptions>(new WhatsAppOptions()),
                time),
            WhatsAppTenant.Default);
    }
}
