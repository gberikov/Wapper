using System.Text.Json.Serialization;

namespace Wapper.Internal;

/// <summary>Cursor paging, which every Graph collection uses the same way.</summary>
internal sealed class GraphPagingPayload
{
    [JsonPropertyName("cursors")]
    public GraphCursorsPayload? Cursors { get; set; }

    /// <summary>
    /// Absent on the last page. Meta signals the end by leaving this out rather than by
    /// sending an empty cursor, which it still sends.
    /// </summary>
    [JsonPropertyName("next")]
    public string? Next { get; set; }

    /// <summary>
    /// The cursor to ask for the next page with, or <see langword="null"/> when this was the
    /// last one.
    /// </summary>
    public string? NextCursor => Next is null ? null : Cursors?.After;
}

internal sealed class GraphCursorsPayload
{
    [JsonPropertyName("after")]
    public string? After { get; set; }
}
