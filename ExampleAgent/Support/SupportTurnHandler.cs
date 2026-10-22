using ExampleAgent.Shop;
using Microsoft.Extensions.AI;
using Suwageval.AgentSdk.Invocation;

namespace ExampleAgent.Support;

/// <summary>One turn: the conversation in, what the support agent produced out (<c>POST /suwageval/turn</c>).</summary>
internal sealed partial class SupportTurnHandler(IChatClient chatClient, ShopStore shops, ILogger<SupportTurnHandler> logger) : ITurnHandler
{
    public async Task<TurnResponse> HandleAsync(TurnRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The headers are for correlation only: they go to the logs, never into what the agent does.
        LogTurn(request.Correlation.RunId, request.Correlation.Scenario, request.Correlation.Sample, request.ConversationId);

        var messages = ChatMapping.ToMessages(request); // throws TurnRejectedException for a history that cannot be one
        var shop = shops.For(request.ConversationId, request.FixturesAs<ShopFixtures>());
        var response = await SupportAgent.Create(chatClient, shop).RunAsync(messages, cancellationToken: cancellationToken);
        return new TurnResponse { Items = ChatMapping.ToItems(response.Messages, SupportAgent.Name) };
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Turn of run {RunId}, scenario {Scenario}, sample {Sample}, conversation {ConversationId}"
    )]
    private partial void LogTurn(string? runId, string? scenario, string? sample, string? conversationId);
}
