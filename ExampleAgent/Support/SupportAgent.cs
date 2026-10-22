using ExampleAgent.Shop;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ExampleAgent.Support;

/// <summary>The one LLM agent of this system: Microsoft Agent Framework over any chat client.</summary>
internal static class SupportAgent
{
    /// <summary>Its name in the contract and on every tool call it makes (<c>by: support</c> in a check).</summary>
    public const string Name = "support";

    public const string SystemPrompt = """
        You are a polite customer support agent for Kettle & Co., an online shop.
        Answer in the language the customer writes in.
        Use get_order to look up an order before you talk about it or refund it.
        Never refund an order without an order id: if the customer has not given one, ask for it.
        Always look the order up with get_order before calling refund_order; refund only what the lookup shows is delivered.
        For a question about refunds, shipping or the warranty, use search_policy and answer only from what it returns.
        Keep answers short.
        """;

    /// <summary>The agent over one shop (its tools work on that shop's orders).</summary>
    public static AIAgent Create(IChatClient chatClient, Shop.Shop shop)
    {
        ArgumentNullException.ThrowIfNull(shop);
        return new ChatClientAgent(chatClient, SystemPrompt, Name, description: null, tools: [.. new ShopTools(shop).ToFunctions()]);
    }
}
