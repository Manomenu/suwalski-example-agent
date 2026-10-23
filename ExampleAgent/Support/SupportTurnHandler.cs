using System.Diagnostics;
using ExampleAgent.Shop;
using ExampleAgent.Tracing;
using Microsoft.Extensions.AI;
using Suwageval.AgentSdk.Invocation;

namespace ExampleAgent.Support;

/// <summary>One turn: the conversation in, what the support agent produced out (<c>POST /suwageval/turn</c>).</summary>
internal sealed partial class SupportTurnHandler(
    IChatClient chatClient,
    ShopStore shops,
    PromptSettings prompt,
    TracingSettings tracing,
    ILogger<SupportTurnHandler> logger
) : ITurnHandler
{
    public async Task<TurnResponse> HandleAsync(TurnRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var turn = AgentTracing.StartTurn(request);
        // The headers are for correlation only: they go to the logs, never into what the agent does.
        LogTurn(request.Correlation.RunId, request.Correlation.Scenario, request.Correlation.Sample, request.ConversationId);

        AgentTracing.RecordInput(turn, request, tracing.SensitiveData);
        try
        {
            var messages = ChatMapping.ToMessages(request); // throws TurnRejectedException for a history that cannot be one
            var shop = shops.For(request.ConversationId, request.FixturesAs<ShopFixtures>());
            var response = await SupportAgent
                .Create(chatClient, shop, prompt.Text, tracing.SensitiveData)
                .RunAsync(messages, cancellationToken: cancellationToken);
            var result = new TurnResponse { Items = ChatMapping.ToItems(response.Messages, SupportAgent.Name) };
            AgentTracing.RecordOutput(turn, result, tracing.SensitiveData);
            return result;
        }
        catch (Exception error)
        {
            turn?.SetStatus(ActivityStatusCode.Error, error.Message); // the processor turns it into level ERROR
            throw;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Turn of run {RunId}, scenario {Scenario}, sample {Sample}, conversation {ConversationId}"
    )]
    private partial void LogTurn(string? runId, string? scenario, string? sample, string? conversationId);
}
