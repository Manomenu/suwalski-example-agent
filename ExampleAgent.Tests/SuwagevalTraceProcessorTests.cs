using System.Diagnostics;
using ExampleAgent.Tracing;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Suwageval.AgentSdk.Invocation;

namespace ExampleAgent.Tests;

/// <summary>Keeps the spans it sees (what the exporter would get).</summary>
internal sealed class CollectingProcessor : BaseProcessor<Activity>
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<Activity> ended = new();

    // Turns of tests running in parallel end on several threads at once.
    public IReadOnlyCollection<Activity> Ended => ended;

    public override void OnEnd(Activity data) => ended.Enqueue(data);
}

public class SuwagevalTraceProcessorTests
{
    private static (TracerProvider Provider, CollectingProcessor Spans) Start()
    {
        var spans = new CollectingProcessor();
        var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(AgentTracing.SourceName)
            .AddProcessor(new SuwagevalTraceProcessor())
            .AddProcessor(spans)
            .Build();
        return (provider, spans);
    }

    private static readonly string[] ExpectedTags =
    [
        "suwageval_run:run-1",
        "suwageval_scenario_tag:a",
        "suwageval_scenario_tag:b",
        "suwageval_run_tag:fast",
        "suwageval_run_group:group-1",
    ];

    private static TurnRequest Request(TurnCorrelation correlation, string? conversationId) =>
        new()
        {
            Conversation = [],
            Correlation = correlation,
            ConversationId = conversationId,
        };

    [Fact]
    public void Suwageval_context_lands_on_the_turn_and_on_its_children()
    {
        var (provider, spans) = Start();
        using (provider)
        {
            var correlation = new TurnCorrelation("run-1", "refunds", ScenarioType.Simulation, "2", ["a", "b"], ["fast"], "group-1");
            using (var turn = AgentTracing.StartTurn(Request(correlation, "conversation-9")))
            {
                using var child = AgentTracing.Source.StartActivity("child");
            }
        }

        Assert.Equal(2, spans.Ended.Count);
        foreach (var span in spans.Ended)
        {
            Assert.Equal("conversation-9", span.GetTagItem("langfuse.session.id"));
            Assert.Equal("suwageval", span.GetTagItem("langfuse.environment"));
            Assert.Equal("run-1", span.GetTagItem("langfuse.trace.metadata.suwageval_run_id"));
            Assert.Equal("refunds", span.GetTagItem("langfuse.trace.metadata.suwageval_scenario"));
            Assert.Equal("2", span.GetTagItem("langfuse.trace.metadata.suwageval_sample"));
            Assert.Equal("Simulation", span.GetTagItem("langfuse.trace.metadata.suwageval_scenario_type"));
            Assert.Equal("group-1", span.GetTagItem("langfuse.trace.metadata.suwageval_run_group"));
            Assert.Equal(ExpectedTags, Assert.IsType<string[]>(span.GetTagItem("langfuse.trace.tags")));
        }
    }

    [Fact]
    public void Without_baggage_the_turn_has_no_suwageval_values_and_the_default_environment()
    {
        var (provider, spans) = Start();
        using (provider)
        {
            using var turn = AgentTracing.StartTurn(Request(TurnCorrelation.None, null));
        }

        var span = Assert.Single(spans.Ended);
        Assert.Equal("turn", span.OperationName);
        Assert.DoesNotContain(span.TagObjects, tag => tag.Key.StartsWith("langfuse.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_conversation_id_alone_is_only_the_session()
    {
        var (provider, spans) = Start();
        using (provider)
        {
            using var turn = AgentTracing.StartTurn(Request(TurnCorrelation.None, "conversation-3"));
        }

        var span = Assert.Single(spans.Ended);
        Assert.Equal("conversation-3", span.GetTagItem("langfuse.session.id"));
        Assert.Null(span.GetTagItem("langfuse.environment"));
        Assert.Null(span.GetTagItem("langfuse.trace.tags"));
    }

    [Fact]
    public void A_model_call_is_a_generation_and_a_tool_call_a_tool()
    {
        var (provider, spans) = Start();
        using (provider)
        {
            using var chat = AgentTracing.Source.StartActivity("chat m");
            chat?.SetTag("gen_ai.operation.name", "chat");
            using var tool = AgentTracing.Source.StartActivity("execute_tool t");
            tool?.SetTag("gen_ai.operation.name", "execute_tool");
        }

        Assert.Equal(
            ["tool", "generation"],
            spans.Ended.Select(span => span.GetTagItem("langfuse.observation.type")).Cast<string>().ToArray()
        );
    }

    [Fact]
    public void Authorization_is_built_from_the_keys_and_missing_keys_turn_tracing_off()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["LANGFUSE_PUBLIC_KEY"] = "pk", ["LANGFUSE_SECRET_KEY"] = "sk" })
            .Build();
        Assert.Equal("Authorization=Basic cGs6c2s=", AgentTracing.AuthorizationHeader(configuration));
        Assert.Null(AgentTracing.AuthorizationHeader(new ConfigurationBuilder().Build()));
    }
}
