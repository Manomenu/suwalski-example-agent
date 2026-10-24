using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Suwageval.AgentSdk.Conversations;
using Suwageval.AgentSdk.Monitoring;

namespace ExampleAgent.Tests;

public class ChatTests
{
    /// <summary>Keeps what the chat records, in place of Suwageval's monitoring.</summary>
    private sealed class Recorded : IConversationRecorder
    {
        public List<(string ConversationId, IReadOnlyList<ConversationItem> Items, string? DataVersion)> Turns { get; } = [];

        public void Record(string conversationId, IReadOnlyList<ConversationItem> items, string? dataVersion = null) =>
            Turns.Add((conversationId, items, dataVersion));
    }

    private static ScriptedChatClient Answers(string answer) => new(_ => new ChatMessage(ChatRole.Assistant, answer));

    private static async Task<string> Say(TestAgent agent, string conversationId, string message)
    {
        var response = await agent.Client.PostAsJsonAsync("/chat", new { conversationId, message });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("answer").GetString()!;
    }

    [Fact]
    public async Task A_chat_turn_answers_remembers_the_conversation_and_is_recorded_with_the_policy_data()
    {
        var recorded = new Recorded();
        var model = Answers("Hello!");
        using var agent = new TestAgent(model, recorded);

        Assert.Equal("Hello!", await Say(agent, "chat-1", "Hi"));
        await Say(agent, "chat-1", "Where is A-17?");

        Assert.Equal(
            ["Hi", "Hello!", "Where is A-17?"],
            model.Calls[^1].Where(message => message.Role != ChatRole.System).Select(message => message.Text)
        );
        Assert.Equal(2, recorded.Turns.Count);
        Assert.All(
            recorded.Turns,
            turn => Assert.Equal(("chat-1", Policy.PolicyIndex.DataVersion), (turn.ConversationId, turn.DataVersion))
        );
        Assert.Collection(
            recorded.Turns[1].Items,
            item => Assert.Equal("Where is A-17?", Assert.IsType<UserMessage>(item).Text),
            item => Assert.Equal("Hello!", Assert.IsType<AssistantMessage>(item).Text)
        );
    }

    [Fact]
    public async Task The_chat_works_on_the_demo_shop_and_a_turn_of_an_evaluation_is_not_recorded()
    {
        var recorded = new Recorded();
        using var agent = new TestAgent(ScriptedChatClient.CallsOnce("get_order", new() { ["order_id"] = "A-17" }, "Delivered."), recorded);

        await Say(agent, "chat-2", "Where is A-17?");
        await TestAgent.ItemsOf(await agent.PostAsync(TestAgent.UserSays("Where is A-17?")));

        var turn = Assert.Single(recorded.Turns);
        Assert.Contains("delivered", turn.Items.OfType<ToolResult>().Single().Content, StringComparison.Ordinal);
    }
}
