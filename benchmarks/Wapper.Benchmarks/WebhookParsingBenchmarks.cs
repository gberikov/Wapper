using System.Text;
using BenchmarkDotNet.Attributes;
using Wapper.Webhooks;

namespace Wapper.Benchmarks;

/// <summary>
/// Parsing one delivery into events, for batches of typed messages, batches with items the
/// parser cannot read, and batches of message types it does not know.
/// </summary>
/// <remarks>
/// Every item is bound on its own, so the cost grows with the number of items and the size
/// of each; a mixed batch pays for the raw JSON of the items it reports. Time and allocated
/// bytes are both interesting: the parser runs on the request path of a public endpoint.
/// </remarks>
[MemoryDiagnoser]
[ShortRunJob]
public class WebhookParsingBenchmarks
{
    private byte[] _typed = [];
    private byte[] _mixed = [];
    private byte[] _unknown = [];

    /// <summary>Messages per delivery. Meta sends one, occasionally a handful.</summary>
    [Params(1, 10, 100)]
    public int Items { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _typed = Delivery(Enumerable.Range(0, Items).Select(Text));
        _mixed = Delivery(Enumerable.Range(0, Items).Select(i => i % 2 == 0 ? Text(i) : Malformed(i)));
        _unknown = Delivery(Enumerable.Range(0, Items).Select(Hologram));
    }

    [Benchmark(Baseline = true)]
    public int Typed() => WhatsAppWebhookParser.Parse(_typed).Count;

    [Benchmark]
    public int MixedWithUnreadable() => WhatsAppWebhookParser.Parse(_mixed).Count;

    [Benchmark]
    public int UnknownTypes() => WhatsAppWebhookParser.Parse(_unknown).Count;

    private static string Text(int i) =>
        $$$"""{"from":"79000000001","id":"wamid.{{{i}}}","timestamp":"1755000000","type":"text","text":{"body":"hello number {{{i}}}, how are you today?"}}""";

    private static string Malformed(int i) =>
        $$$"""{"from":"79000000001","id":"wamid.{{{i}}}","timestamp":"1755000000","type":"text","text":{"body":{{{i}}}}}""";

    private static string Hologram(int i) =>
        $$$"""{"from":"79000000001","id":"wamid.{{{i}}}","timestamp":"1755000000","type":"hologram","hologram":{"frames":[1,2,3],"caption":"item {{{i}}}"}}""";

    private static byte[] Delivery(IEnumerable<string> messages) => Encoding.UTF8.GetBytes(
        """{"object":"whatsapp_business_account","entry":[{"id":"102290129340398","changes":[{"field":"messages","value":{"messaging_product":"whatsapp","metadata":{"display_phone_number":"15550001111","phone_number_id":"106540352242922"},"contacts":[{"profile":{"name":"Ada"},"wa_id":"79000000001"}],"messages":["""
        + string.Join(",", messages)
        + """]}}]}]}""");
}
