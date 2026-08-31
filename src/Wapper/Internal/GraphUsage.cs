using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wapper.Internal;

/// <summary>Wire shape of the <c>X-App-Usage</c> header.</summary>
internal sealed class AppUsage
{
    [JsonPropertyName("call_count")]
    public int CallCount { get; set; }

    [JsonPropertyName("total_time")]
    public int TotalTime { get; set; }

    [JsonPropertyName("total_cputime")]
    public int TotalCpuTime { get; set; }

    public int Highest => Math.Max(CallCount, Math.Max(TotalTime, TotalCpuTime));
}

/// <summary>One entry of the <c>X-Business-Use-Case-Usage</c> header.</summary>
internal sealed class BusinessUseCaseUsage
{
    /// <summary>
    /// The kind of limit. Meta's documented values do not include WhatsApp at all even
    /// though the management API is governed by these limits, so this is read as an open
    /// string and never matched against an enum.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("call_count")]
    public int CallCount { get; set; }

    [JsonPropertyName("total_time")]
    public int TotalTime { get; set; }

    [JsonPropertyName("total_cputime")]
    public int TotalCpuTime { get; set; }

    /// <summary>Minutes until calls stop being throttled. Meta's nearest thing to Retry-After.</summary>
    [JsonPropertyName("estimated_time_to_regain_access")]
    public int EstimatedTimeToRegainAccessMinutes { get; set; }

    public int Highest => Math.Max(CallCount, Math.Max(TotalTime, TotalCpuTime));
}

/// <summary>What the usage headers of one response said.</summary>
/// <param name="HighestPercent">
/// The largest of the reported percentages, or zero when the header was absent. Meta starts
/// throttling at 100.
/// </param>
/// <param name="TimeToRegainAccess">
/// How long until the block lifts, when the response said so.
/// </param>
internal readonly record struct UsageReading(int HighestPercent, TimeSpan TimeToRegainAccess)
{
    public static readonly UsageReading None = new(0, TimeSpan.Zero);

    public bool IsOverThreshold(int threshold) =>
        HighestPercent >= threshold || TimeToRegainAccess > TimeSpan.Zero;
}

/// <summary>
/// Reads Meta's usage headers. They are the only forward-looking signal available: the
/// Cloud API sends no <c>Retry-After</c>, and the app-level budget has no published size,
/// so these percentages are the only way to see a wall before hitting it.
/// </summary>
internal static class GraphUsageHeaders
{
    public const string AppUsageHeader = "X-App-Usage";
    public const string BusinessUseCaseUsageHeader = "X-Business-Use-Case-Usage";

    /// <summary>Reads <c>X-App-Usage</c>.</summary>
    public static UsageReading ReadAppUsage(HttpResponseMessage response)
    {
        var raw = ReadHeader(response, AppUsageHeader);
        if (raw is null)
        {
            return UsageReading.None;
        }

        var usage = Deserialise(raw, WhatsAppJsonContext.Default.AppUsage);
        return usage is null ? UsageReading.None : new UsageReading(usage.Highest, TimeSpan.Zero);
    }

    /// <summary>Reads <c>X-Business-Use-Case-Usage</c>, taking the worst entry it reports.</summary>
    public static UsageReading ReadBusinessUseCaseUsage(HttpResponseMessage response)
    {
        var raw = ReadHeader(response, BusinessUseCaseUsageHeader);
        if (raw is null)
        {
            return UsageReading.None;
        }

        // Keyed by business object id, each value a list. Meta documents up to 32 objects in
        // one header and does not say which one is ours, so the worst entry wins.
        var byBusiness = Deserialise(raw, WhatsAppJsonContext.Default.DictionaryStringListBusinessUseCaseUsage);
        if (byBusiness is null)
        {
            return UsageReading.None;
        }

        var highest = 0;
        var minutes = 0;

        foreach (var entries in byBusiness.Values)
        {
            foreach (var entry in entries)
            {
                highest = Math.Max(highest, entry.Highest);
                minutes = Math.Max(minutes, entry.EstimatedTimeToRegainAccessMinutes);
            }
        }

        return new UsageReading(highest, TimeSpan.FromMinutes(minutes));
    }

    /// <remarks>
    /// Enumerated by hand rather than with <c>FirstOrDefault</c>: this runs on every response
    /// the client receives, and the header is usually absent, so the allocation of an
    /// enumerator is the whole cost of the call.
    /// </remarks>
    private static string? ReadHeader(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            return value;
        }

        return null;
    }

    private static T? Deserialise<T>(string raw, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        where T : class
    {
        // These headers are diagnostics. A malformed one must never take down a call that
        // otherwise succeeded, and Meta's own documentation prints a sample with a duplicate
        // key in it.
        try
        {
            return JsonSerializer.Deserialize(raw, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
