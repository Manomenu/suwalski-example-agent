using Microsoft.Extensions.AI;
using Suwageval.AgentSdk.Conversations;

namespace ExampleAgent.Tests;

public class TurnTests
{
    private static ScriptedChatClient Refunds() =>
        ScriptedChatClient.CallsOnce("refund_order", new() { ["order_id"] = "A-17", ["reason"] = "broken" }, "Done.");

    private static bool RefundFailed(IReadOnlyList<ConversationItem> items) => items.OfType<ToolResult>().Single().IsError;

    [Fact]
    public async Task A_refund_gives_the_tool_call_its_result_the_state_event_and_the_answer()
    {
        using var agent = new TestAgent(Refunds());

        var items = await TestAgent.ItemsOf(
            await agent.PostAsync(TestAgent.UserSays("Refund A-17", TestAgent.Orders("A-17", "delivered")))
        );

        Assert.Collection(
            items,
            item => Assert.Equal("support", Assert.IsType<ToolCall>(item).Agent),
            item => Assert.False(Assert.IsType<ToolResult>(item).IsError),
            item => Assert.Equal("state.changed", Assert.IsType<Event>(item).Type),
            item => Assert.Equal("Done.", Assert.IsType<AssistantMessage>(item).Text)
        );
        Assert.Equal("""{"node":"refunded","orderId":"A-17"}""", items.OfType<Event>().Single().Data.GetRawText());
    }

    [Fact]
    public async Task A_refund_of_an_undelivered_order_is_an_error_without_an_event()
    {
        using var agent = new TestAgent(Refunds());

        var items = await TestAgent.ItemsOf(await agent.PostAsync(TestAgent.UserSays("Refund A-17", TestAgent.Orders("A-17", "shipped"))));

        Assert.True(RefundFailed(items));
        Assert.Empty(items.OfType<Event>());
    }

    [Fact]
    public async Task Without_a_conversation_id_every_turn_starts_from_the_fixtures()
    {
        using var agent = new TestAgent(Refunds());
        var turn = TestAgent.UserSays("Refund A-17", TestAgent.Orders("A-17", "delivered"));

        Assert.False(RefundFailed(await TestAgent.ItemsOf(await agent.PostAsync(turn))));
        Assert.False(RefundFailed(await TestAgent.ItemsOf(await agent.PostAsync(turn))));
    }

    [Fact]
    public async Task With_a_conversation_id_the_shop_goes_on_from_what_earlier_turns_left()
    {
        using var agent = new TestAgent(Refunds());
        var fixtures = TestAgent.Orders("A-17", "delivered");

        Assert.False(
            RefundFailed(await TestAgent.ItemsOf(await agent.PostAsync(TestAgent.UserSays("Refund A-17", fixtures, "conversation-1"))))
        );
        Assert.True(
            RefundFailed(await TestAgent.ItemsOf(await agent.PostAsync(TestAgent.UserSays("Refund A-17", fixtures, "conversation-1"))))
        );
        // another conversation has a shop of its own, fresh from the fixtures
        Assert.False(
            RefundFailed(await TestAgent.ItemsOf(await agent.PostAsync(TestAgent.UserSays("Refund A-17", fixtures, "conversation-2"))))
        );
    }

    [Fact]
    public async Task The_history_is_replayed_to_the_model_with_tool_calls_paired_to_their_results()
    {
        var model = new ScriptedChatClient(_ => new ChatMessage(ChatRole.Assistant, "Sure."));
        using var agent = new TestAgent(model);
        var conversation = new object[]
        {
            new { kind = "UserMessage", text = "Where is A-17?" },
            new
            {
                kind = "ToolCall",
                id = "c1",
                name = "get_order",
                arguments = new { order_id = "A-17" },
                agent = "support",
            },
            new
            {
                kind = "ToolResult",
                callId = "c1",
                content = """{"status":"delivered"}""",
            },
            new { kind = "AssistantMessage", text = "Delivered." },
            new { kind = "UserMessage", text = "Refund it, please." },
        };

        await TestAgent.ItemsOf(await agent.PostAsync(new { conversation }));

        var sent = model.Calls.Single();
        Assert.Collection(
            sent.Where(m => m.Role != ChatRole.System),
            m => Assert.Equal(ChatRole.User, m.Role),
            m => Assert.Equal("c1", Assert.IsType<FunctionCallContent>(Assert.Single(m.Contents)).CallId),
            m => Assert.Equal("c1", Assert.IsType<FunctionResultContent>(Assert.Single(m.Contents)).CallId),
            m => Assert.Equal("Delivered.", m.Text),
            m => Assert.Equal("Refund it, please.", m.Text)
        );
    }

    [Fact]
    public async Task A_history_that_cannot_be_one_is_rejected_with_400()
    {
        using var agent = new TestAgent(Refunds());
        var conversation = new object[]
        {
            new { kind = "UserMessage", text = "Hi" },
            new
            {
                kind = "ToolResult",
                callId = "nobody-called-this",
                content = "{}",
            },
            new { kind = "UserMessage", text = "Refund A-17" },
        };

        var response = await agent.PostAsync(new { conversation });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "nobody-called-this",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Fixtures_of_another_shape_are_rejected_with_400()
    {
        using var agent = new TestAgent(Refunds());
        var fixtures = new { orders = new { A17 = new { status = "lost" } } };

        var response = await agent.PostAsync(TestAgent.UserSays("Refund A-17", fixtures));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_policy_search_is_followed_by_the_retrieval_and_the_answer_cites_its_sources()
    {
        using var agent = new TestAgent(
            ScriptedChatClient.CallsOnce("search_policy", new() { ["query"] = "refund of a shipped order" }, "Not before it is delivered.")
        );

        var items = await TestAgent.ItemsOf(await agent.PostAsync(TestAgent.UserSays("Can I get a refund while it is shipped?")));

        var retrieval = items.OfType<Retrieval>().Single();
        Assert.Equal("refund-policy", retrieval.Chunks[0].Source);
        Assert.Contains("refund-policy", items.OfType<AssistantMessage>().Single().Sources!);
    }
}
