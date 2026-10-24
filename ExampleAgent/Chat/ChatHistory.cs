using System.Collections.Concurrent;
using Suwageval.AgentSdk.Conversations;

namespace ExampleAgent.Chat;

/// <summary>
/// The conversations of the chat so far, by id, in the conversation log's items (what the agent replays as history). In memory and never
/// evicted — fine for an example, not for production.
/// </summary>
internal sealed class ChatHistory
{
    private readonly ConcurrentDictionary<string, List<ConversationItem>> conversations = new();

    public IReadOnlyList<ConversationItem> Of(string conversationId)
    {
        var items = conversations.GetOrAdd(conversationId, _ => []);
        lock (items)
        {
            return [.. items];
        }
    }

    public void Append(string conversationId, IEnumerable<ConversationItem> turn)
    {
        var items = conversations.GetOrAdd(conversationId, _ => []);
        lock (items)
        {
            items.AddRange(turn);
        }
    }
}
