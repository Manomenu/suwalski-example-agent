namespace ExampleAgent.Chat;

/// <summary>A customer's message in a conversation of the shop's chat (<c>POST /chat</c>).</summary>
internal sealed record ChatRequest(string ConversationId, string Message);
