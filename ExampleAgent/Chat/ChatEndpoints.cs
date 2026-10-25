using ExampleAgent.Policy;
using ExampleAgent.Shop;
using ExampleAgent.Support;
using ExampleAgent.Tracing;
using Microsoft.Extensions.AI;
using Suwageval.AgentSdk.Conversations;
using Suwageval.AgentSdk.Invocation;
using Suwageval.AgentSdk.Monitoring;

namespace ExampleAgent.Chat;

/// <summary>
/// The shop's real chat, what a customer talks to (Suwageval's evaluation uses <c>POST /suwageval/turn</c> instead). Each turn is recorded
/// for Suwageval's monitoring: with <c>Suwageval:Url</c> set it goes there in the background, without it nowhere. <c>GET /</c> is a page
/// to talk to it by hand (ChatPage.html).
/// </summary>
internal static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChat(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", () => Results.Content(Page.Value, "text/html; charset=utf-8"));
        app.MapPost("/chat", ChatAsync);
        return app;
    }

    /// <summary>The chat page, embedded in the assembly so it is there however the agent runs (from source, the image, the tests).</summary>
    private static readonly Lazy<string> Page = new(() =>
    {
        using var stream =
            typeof(ChatEndpoints).Assembly.GetManifestResourceStream("ExampleAgent.Chat.ChatPage.html")
            ?? throw new InvalidOperationException("ChatPage.html is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    private static async Task<IResult> ChatAsync(
        ChatRequest request,
        IChatClient chatClient,
        ShopStore shops,
        PromptSettings prompt,
        TracingSettings tracing,
        ChatHistory history,
        IConversationRecorder recorder,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(request.ConversationId) || string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["message"] = ["A conversation id and a message."] });
        }
        var user = new UserMessage { Text = request.Message };
        var turn = new TurnRequest { Conversation = [.. history.Of(request.ConversationId), user] };
        var shop = shops.For(request.ConversationId, ShopFixtures.Demo);
        var response = await SupportAgent
            .Create(chatClient, shop, prompt.Text, tracing.SensitiveData)
            .RunAsync(ChatMapping.ToMessages(turn), cancellationToken: cancellationToken);
        var items = ChatMapping.ToItems(response.Messages, SupportAgent.Name);
        history.Append(request.ConversationId, [user, .. items]);
        recorder.Record(request.ConversationId, [user, .. items], PolicyIndex.DataVersion);
        var said = items.OfType<AssistantMessage>().ToList();
        return Results.Ok(
            new ChatResponse(said.LastOrDefault()?.Text ?? "", [.. said.SelectMany(message => message.Sources ?? []).Distinct()])
        );
    }
}
