using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using ExampleAgent.Tracing;

namespace ExampleAgent.Policy;

/// <summary>A paragraph of the shop's policy and the source id an answer can cite it by.</summary>
internal sealed record PolicyChunk(string Source, string Text);

/// <summary>
/// A tiny retrieval: a few policy paragraphs in code and a keyword match over them. It exists so the agent has something to
/// retrieve (<c>Retrieval</c> items, <c>sources</c> on its answers, a <c>cited</c> check), not to be a search engine.
/// </summary>
internal static class PolicyIndex
{
    public const string ToolName = "search_policy";

    private static readonly PolicyChunk[] Chunks =
    [
        new(
            "refund-policy",
            "Refunds: only a delivered order can be refunded. An order that is still processing or shipped cannot be refunded yet; "
                + "the customer can ask again once it has been delivered. An order that is already refunded cannot be refunded twice."
        ),
        new(
            "shipping-policy",
            "Shipping: orders are processed within 1 business day and delivered within 3 to 5 business days. "
                + "Shipping is free for orders over 100 PLN."
        ),
        new(
            "warranty-policy",
            "Warranty: all kettles and small appliances have a 24 month warranty. A product that arrives broken or stops working "
                + "within the warranty period is refunded or replaced."
        ),
    ];

    /// <summary>The version of what retrieval can return: it changes whenever a paragraph does (<c>context.retrieval.dataVersion</c>).</summary>
    public static string DataVersion { get; } =
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', Chunks.Select(c => $"{c.Source}\t{c.Text}")))))[
            ..12
        ];

    public static string Description { get; } = $"{Chunks.Length} policy paragraphs, matched by keyword";

    /// <summary>The paragraphs sharing the most words with the query, best first (at most two); none when no word is shared.</summary>
    public static IReadOnlyList<(PolicyChunk Chunk, double Score)> Search(string query)
    {
        var words = Words(query);
        return
        [
            .. Chunks
                .Select(chunk =>
                    (Chunk: chunk, Score: words.Count == 0 ? 0 : (double)Words(chunk.Text).Intersect(words).Count() / words.Count)
                )
                .Where(match => match.Score > 0)
                .OrderByDescending(match => match.Score)
                .Take(2),
        ];
    }

    [Description("Search the shop's policies (refunds, shipping, warranty). Use it for any question about how the shop works.")]
    public static object SearchPolicy(
        [Description("What the customer asks about, in a few words, e.g. refund of a shipped order")] string query
    )
    {
        using var retrieval = AgentTracing.Source.StartActivity("retrieval");
        retrieval?.SetTag("langfuse.observation.type", "retriever");
        retrieval?.SetTag("langfuse.observation.input", query);
        var chunks = Search(query).Select(match => new { source = match.Chunk.Source, text = match.Chunk.Text }).ToArray();
        retrieval?.SetTag("langfuse.observation.output", System.Text.Json.JsonSerializer.Serialize(chunks));
        return chunks;
    }

    private static HashSet<string> Words(string text) =>
        text.Split([' ', ',', '.', ':', ';', '?', '!', '-', '(', ')', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.ToLowerInvariant())
            .Where(word => word.Length > 3)
            .ToHashSet();
}
