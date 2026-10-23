using System.Diagnostics;
using System.Text.Json;
using ExampleAgent.Tracing;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Suwageval.AgentSdk.Conversations;
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
    private static (TracerProvider Provider, CollectingProcessor Spans) Start(string? version = null, string? release = null)
    {
        var spans = new CollectingProcessor();
        var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(AgentTracing.SourceName)
            .AddProcessor(new SuwagevalTraceProcessor(version, release))
            .AddProcessor(spans)
            .Build();
        return (provider, spans);
    }

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
            Assert.Equal("run-1", span.GetTagItem("langfuse.trace.metadata.suwageval.run.id"));
            Assert.Equal("refunds", span.GetTagItem("langfuse.trace.metadata.suwageval.scenario.key"));
            Assert.Equal("2", span.GetTagItem("langfuse.trace.metadata.suwageval.scenario.sample"));
            Assert.Equal("Simulation", span.GetTagItem("langfuse.trace.metadata.suwageval.scenario.type"));
            Assert.Equal("group-1", span.GetTagItem("langfuse.trace.metadata.suwageval.run.group"));
            Assert.Equal("a,b", span.GetTagItem("langfuse.trace.metadata.suwageval.scenario.tags"));
            Assert.Equal("fast", span.GetTagItem("langfuse.trace.metadata.suwageval.run.tags"));
            Assert.Null(span.GetTagItem("langfuse.trace.tags")); // no Langfuse tags: every value could be new on any run
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
        Assert.DoesNotContain(
            span.TagObjects,
            tag => tag.Key.StartsWith("langfuse.", StringComparison.Ordinal) && tag.Key != "langfuse.observation.type"
        );
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
    public void The_turn_is_an_agent_and_the_tool_calling_loop_a_chain()
    {
        var (provider, spans) = Start();
        using (provider)
        {
            using var turn = AgentTracing.StartTurn(Request(TurnCorrelation.None, null));
            using var loop = AgentTracing.Source.StartActivity("orchestrate_tools");
        }

        Assert.Equal(["chain", "agent"], spans.Ended.Select(span => span.GetTagItem("langfuse.observation.type")).Cast<string>().ToArray());
    }

    [Fact]
    public void Version_and_release_are_on_every_span_even_without_suwageval()
    {
        var (provider, spans) = Start("concise", "abc123");
        using (provider)
        {
            using var turn = AgentTracing.StartTurn(Request(TurnCorrelation.None, null));
            using var child = AgentTracing.Source.StartActivity("child");
        }

        Assert.Equal(2, spans.Ended.Count);
        Assert.All(
            spans.Ended,
            span =>
            {
                Assert.Equal("concise", span.GetTagItem("langfuse.version"));
                Assert.Equal("abc123", span.GetTagItem("langfuse.release"));
            }
        );
    }

    [Fact]
    public void Without_a_label_or_release_the_spans_carry_neither()
    {
        var (provider, spans) = Start();
        using (provider)
        {
            using var turn = AgentTracing.StartTurn(Request(TurnCorrelation.None, null));
        }

        var span = Assert.Single(spans.Ended);
        Assert.Null(span.GetTagItem("langfuse.version"));
        Assert.Null(span.GetTagItem("langfuse.release"));
    }

    [Fact]
    public void The_agent_version_id_is_metadata_on_every_span()
    {
        var (provider, spans) = Start();
        using (provider)
        {
            var correlation = TurnCorrelation.None with { AgentVersionId = "version-7" };
            using var turn = AgentTracing.StartTurn(Request(correlation, null));
            using var child = AgentTracing.Source.StartActivity("child");
        }

        Assert.Equal(2, spans.Ended.Count);
        Assert.All(
            spans.Ended,
            span =>
            {
                Assert.Equal("version-7", span.GetTagItem("langfuse.trace.metadata.suwageval.agent.version.id"));
                Assert.Equal("suwageval", span.GetTagItem("langfuse.environment"));
            }
        );
    }

    [Fact]
    public void The_turn_carries_the_conversation_and_the_answer_as_input_and_output_on_the_observation_and_the_trace()
    {
        var (provider, spans) = Start();
        using (provider)
        {
            var request = new TurnRequest { Conversation = [new UserMessage { Text = "Hi" }] };
            using var turn = AgentTracing.StartTurn(request);
            AgentTracing.RecordInput(turn, request);
            AgentTracing.RecordOutput(turn, new TurnResponse { Items = [new AssistantMessage { Text = "Hello" }] });
        }

        var span = Assert.Single(spans.Ended);
        foreach (var key in new[] { "langfuse.observation.input", "langfuse.trace.input" })
        {
            Assert.Contains("\"text\":\"Hi\"", (string)span.GetTagItem(key)!, StringComparison.Ordinal);
        }
        foreach (var key in new[] { "langfuse.observation.output", "langfuse.trace.output" })
        {
            Assert.Contains("\"text\":\"Hello\"", (string)span.GetTagItem(key)!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_event_of_the_answer_is_an_event_observation_under_the_turn()
    {
        var (provider, spans) = Start();
        using (provider)
        {
            using var turn = AgentTracing.StartTurn(Request(TurnCorrelation.None, null));
            var data = JsonSerializer.SerializeToElement(new { node = "refunded", orderId = "A-17" });
            AgentTracing.RecordOutput(turn, new TurnResponse { Items = [new Event { Type = "state.changed", Data = data }] });
        }

        var turnSpan = Assert.Single(spans.Ended, span => span.OperationName == "turn");
        var observation = Assert.Single(spans.Ended, span => span.OperationName == "state.changed");
        Assert.Equal(turnSpan.SpanId, observation.ParentSpanId);
        Assert.Equal("event", observation.GetTagItem("langfuse.observation.type"));
        Assert.Equal("{\"node\":\"refunded\",\"orderId\":\"A-17\"}", observation.GetTagItem("langfuse.observation.output"));
        Assert.Equal(observation.GetTagItem("langfuse.observation.output"), observation.GetTagItem("langfuse.observation.input"));
    }

    [Fact]
    public void A_failed_span_is_level_ERROR_with_its_message_and_a_flagged_one_keeps_its_level()
    {
        var (provider, spans) = Start();
        using (provider)
        {
            using (var failed = AgentTracing.Source.StartActivity("failed"))
            {
                failed?.SetStatus(ActivityStatusCode.Error, "boom");
            }
            using (var refused = AgentTracing.Source.StartActivity("refused"))
            {
                AgentTracing.Flag(refused, AgentTracing.Warning, "not delivered");
            }
        }

        var failedSpan = Assert.Single(spans.Ended, span => span.OperationName == "failed");
        Assert.Equal("ERROR", failedSpan.GetTagItem("langfuse.observation.level"));
        Assert.Equal("boom", failedSpan.GetTagItem("langfuse.observation.status_message"));
        var refusedSpan = Assert.Single(spans.Ended, span => span.OperationName == "refused");
        Assert.Equal("WARNING", refusedSpan.GetTagItem("langfuse.observation.level"));
        Assert.Equal("not delivered", refusedSpan.GetTagItem("langfuse.observation.status_message"));
    }

    [Fact]
    public void The_release_is_SUWEXAG_RELEASE_when_set()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SUWEXAG_RELEASE"] = "r-1" })
            .Build();
        Assert.Equal("r-1", AgentTracing.Release(configuration));
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
