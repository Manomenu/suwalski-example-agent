using System.Text.Json;
using System.Text.Json.Nodes;
using ExampleAgent.Policy;
using ExampleAgent.Shop;
using Microsoft.Extensions.AI;
using Suwageval.AgentSdk.Conversations;
using Suwageval.AgentSdk.Invocation;

namespace ExampleAgent.Support;

/// <summary>The mapping between the Suwageval conversation and Agent Framework's chat messages; the only code that knows both.</summary>
internal static class ChatMapping
{
    /// <summary>
    /// Replays the conversation as chat history: <see cref="TurnRequest.HistoryTurns"/> pairs every tool call with its result
    /// (and refuses a history that cannot be one), so the agent sees what it did before and does not run it again.
    /// Retrievals and events are not part of the history.
    /// </summary>
    public static List<ChatMessage> ToMessages(TurnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var messages = new List<ChatMessage>();
        foreach (var turn in request.HistoryTurns())
        {
            if (turn.User is { } user)
            {
                messages.Add(new ChatMessage(ChatRole.User, user.Text));
            }
            foreach (var (call, result) in turn.ToolExchanges)
            {
                messages.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(call.Id, call.Name, ArgumentsOf(call))]));
                messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(result.CallId, result.Content)]));
            }
            if (turn.Assistant is { } assistant)
            {
                messages.Add(new ChatMessage(ChatRole.Assistant, assistant.Text));
            }
        }
        if (request.Conversation[^1] is not UserMessage last)
        {
            throw new TurnRejectedException("The last item of the conversation must be a user message.");
        }
        messages.Add(new ChatMessage(ChatRole.User, last.Text));
        return messages;
    }

    /// <summary>
    /// Turns what the agent produced into conversation items, in order. Every tool call names the LLM agent that made it; a
    /// <c>search_policy</c> result is followed by the <c>Retrieval</c> it stands for, and the answer cites what was retrieved;
    /// a successful <c>refund_order</c> is followed by the <c>state.changed</c> event.
    /// </summary>
    public static List<ConversationItem> ToItems(IEnumerable<ChatMessage> produced, string agent)
    {
        ArgumentNullException.ThrowIfNull(produced);
        var contents = produced.SelectMany(message => message.Contents.Select(content => (message.Role, Content: content))).ToList();
        var calls = contents.Select(c => c.Content).OfType<FunctionCallContent>().ToDictionary(call => call.CallId);
        var sources = calls
            .Values.Where(call => call.Name == PolicyIndex.ToolName)
            .SelectMany(call => PolicyIndex.Search(QueryOf(call)))
            .Select(match => match.Chunk.Source)
            .Distinct()
            .ToList();

        var items = new List<ConversationItem>();
        foreach (var (role, content) in contents)
        {
            switch (content)
            {
                case TextContent { Text.Length: > 0 } text when role == ChatRole.Assistant:
                    items.Add(new AssistantMessage { Text = text.Text, Sources = sources.Count > 0 ? sources : null });
                    break;
                case FunctionCallContent call:
                    items.Add(
                        new ToolCall
                        {
                            Id = call.CallId,
                            Name = call.Name,
                            Arguments = ArgumentsOf(call),
                            Agent = agent,
                        }
                    );
                    break;
                case FunctionResultContent result:
                    var json = ContentOf(result);
                    var isError = result.Exception is not null || HasError(json);
                    items.Add(
                        new ToolResult
                        {
                            CallId = result.CallId,
                            Content = json,
                            IsError = isError,
                        }
                    );
                    items.AddRange(Derived(calls.GetValueOrDefault(result.CallId), isError));
                    break;
            }
        }
        return items;
    }

    private static IEnumerable<ConversationItem> Derived(FunctionCallContent? call, bool isError)
    {
        if (call?.Name == PolicyIndex.ToolName)
        {
            var query = QueryOf(call);
            yield return new Retrieval
            {
                Query = query,
                Chunks =
                [
                    .. PolicyIndex
                        .Search(query)
                        .Select(match => new RetrievedChunk
                        {
                            Text = match.Chunk.Text,
                            Source = match.Chunk.Source,
                            Score = match.Score,
                        }),
                ],
            };
        }
        else if (call?.Name == ShopTools.RefundOrderName && !isError)
        {
            yield return new Event
            {
                Type = SupportContractSource.StateChanged,
                Data = JsonSerializer.SerializeToElement(
                    new { node = "refunded", orderId = ArgumentsOf(call).GetProperty("order_id").GetString() }
                ),
            };
        }
    }

    private static string QueryOf(FunctionCallContent call) =>
        ArgumentsOf(call).TryGetProperty("query", out var query) && query.ValueKind == JsonValueKind.String ? query.GetString() ?? "" : "";

    private static Dictionary<string, object?> ArgumentsOf(ToolCall call) =>
        call.Arguments.ValueKind == JsonValueKind.Object
            ? call.Arguments.Deserialize<Dictionary<string, object?>>() ?? []
            : throw new TurnRejectedException($"The arguments of tool call '{call.Id}' must be an object.");

    private static JsonElement ArgumentsOf(FunctionCallContent call) =>
        JsonSerializer.SerializeToElement(call.Arguments ?? new Dictionary<string, object?>());

    private static string ContentOf(FunctionResultContent result) =>
        result.Result switch
        {
            string text => text,
            null => "null",
            var other => JsonSerializer.Serialize(other),
        };

    /// <summary>A tool failed when it raised or returned <c>{"error": ...}</c>.</summary>
    private static bool HasError(string content)
    {
        try
        {
            return JsonNode.Parse(content) is JsonObject json && json.ContainsKey("error");
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
