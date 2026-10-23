using System.Net.Http.Json;
using ExampleAgent.Tracing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Suwageval.AgentSdk.Conversations;
using Suwageval.AgentSdk.Invocation;

namespace ExampleAgent.Tests;

public class TracingTests
{
    [Fact]
    public async Task A_turn_is_the_root_of_its_trace_with_a_generation_and_a_tool_and_no_http_spans()
    {
        var spans = new CollectingProcessor();
        using var chat = ScriptedChatClient.CallsOnce("refund_order", new() { ["order_id"] = "A-17", ["reason"] = "broken" }, "Done.");
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("OPENAI_BASE_URL", "http://localhost:1/v1");
            host.UseSetting("OPENAI_API_KEY", "not-a-key");
            host.UseSetting("OPENAI_MODEL", "test-model");
            host.UseSetting("LANGFUSE_PUBLIC_KEY", "pk");
            host.UseSetting("LANGFUSE_SECRET_KEY", "sk");
            host.UseSetting("Tracing:SensitiveData", "true");
            host.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:1/api/public/otel"); // nothing listens: exports fail quietly
            host.ConfigureServices(services =>
            {
                services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(chat);
                services.ConfigureOpenTelemetryTracerProvider(builder => builder.AddProcessor(spans));
            });
        });
        var client = factory.CreateClient();
        var conversation = $"conversation-{Guid.NewGuid():N}";

        Assert.Equal(
            System.Net.HttpStatusCode.OK,
            (await client.GetAsync(new Uri("/suwageval/contract", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode
        );
        var response = await client.PostAsJsonAsync(
            new Uri("/suwageval/turn", UriKind.Relative),
            TestAgent.UserSays("Refund A-17", TestAgent.Orders("A-17", "delivered"), conversation),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        // The source is process-wide, so turns of tests running in parallel arrive here too: look at this conversation's trace only.
        // The contract request left nothing: every span belongs to the one turn, and none is an HTTP span.
        var snapshot = spans.Ended.ToArray();
        Assert.All(snapshot, span => Assert.Equal(AgentTracing.SourceName, span.Source.Name));
        var turn = Assert.Single(
            snapshot,
            span => span.OperationName == "turn" && span.GetTagItem("langfuse.session.id") as string == conversation
        );
        Assert.Equal(default, turn.ParentSpanId);
        var trace = snapshot.Where(span => span.TraceId == turn.TraceId).ToArray();
        Assert.Contains(trace, span => span.GetTagItem("langfuse.observation.type") as string == "generation");
        Assert.Contains(trace, span => span.GetTagItem("langfuse.observation.type") as string == "tool");
        Assert.Equal("agent", turn.GetTagItem("langfuse.observation.type"));
        Assert.Contains("Refund A-17", (string)turn.GetTagItem("langfuse.trace.input")!, StringComparison.Ordinal);
        Assert.Contains("Done.", (string)turn.GetTagItem("langfuse.trace.output")!, StringComparison.Ordinal);
        Assert.Contains(trace, span => span.GetTagItem("langfuse.observation.type") as string == "chain");
        Assert.Contains(
            trace,
            span => span.OperationName == "state.changed" && span.GetTagItem("langfuse.observation.type") as string == "event"
        );
        Assert.All(trace, span => Assert.Equal(conversation, span.GetTagItem("langfuse.session.id")));
        Assert.DoesNotContain(snapshot, span => span.OperationName.Contains("HttpRequestIn", StringComparison.Ordinal));
    }

    private static WebApplicationFactory<Program> Factory(
        string publicKey,
        string? sensitive,
        CollectingProcessor? spans,
        IChatClient chat
    ) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("OPENAI_BASE_URL", "http://localhost:1/v1");
            host.UseSetting("OPENAI_API_KEY", "not-a-key");
            host.UseSetting("OPENAI_MODEL", "test-model");
            host.UseSetting("LANGFUSE_PUBLIC_KEY", publicKey);
            host.UseSetting("LANGFUSE_SECRET_KEY", publicKey.Length > 0 ? "sk" : "");
            host.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:1/api/public/otel");
            if (sensitive is not null)
            {
                host.UseSetting("Tracing:SensitiveData", sensitive);
            }
            host.ConfigureServices(services =>
            {
                services.AddSingleton(chat);
                if (spans is not null)
                {
                    services.ConfigureOpenTelemetryTracerProvider(builder => builder.AddProcessor(spans));
                }
            });
        });

    [Fact]
    public async Task Without_sensitive_data_the_turn_has_no_input_or_output_and_no_message_content_anywhere()
    {
        var spans = new CollectingProcessor();
        using var chat = ScriptedChatClient.CallsOnce("refund_order", new() { ["order_id"] = "A-17", ["reason"] = "broken" }, "Done.");
        using var factory = Factory("pk", "false", spans, chat); // set explicitly: a developer's .env turns it on
        var conversation = $"conversation-{Guid.NewGuid():N}";

        var response = await factory
            .CreateClient()
            .PostAsJsonAsync(
                new Uri("/suwageval/turn", UriKind.Relative),
                TestAgent.UserSays("Refund A-17", TestAgent.Orders("A-17", "delivered"), conversation),
                TestContext.Current.CancellationToken
            );
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var snapshot = spans.Ended.ToArray();
        var turn = Assert.Single(
            snapshot,
            span => span.OperationName == "turn" && span.GetTagItem("langfuse.session.id") as string == conversation
        );
        Assert.Null(turn.GetTagItem("langfuse.observation.input"));
        Assert.Null(turn.GetTagItem("langfuse.trace.input"));
        Assert.Null(turn.GetTagItem("langfuse.observation.output"));
        Assert.Null(turn.GetTagItem("langfuse.trace.output"));
        var trace = snapshot.Where(span => span.TraceId == turn.TraceId).ToArray();
        Assert.DoesNotContain(trace, span => span.GetTagItem("gen_ai.input.messages") is not null);
        Assert.DoesNotContain(trace, span => span.GetTagItem("gen_ai.tool.call.arguments") is not null);
    }

    [Fact]
    public async Task The_turn_input_is_the_new_user_message_and_the_output_the_answer_on_the_observation_and_on_the_trace()
    {
        var spans = new CollectingProcessor();
        using var chat = ScriptedChatClient.CallsOnce("refund_order", new() { ["order_id"] = "A-17", ["reason"] = "broken" }, "Done.");
        using var factory = Factory("pk", "true", spans, chat);
        var conversation = $"conversation-{Guid.NewGuid():N}";
        var turnRequest = new
        {
            conversation = new object[]
            {
                new { kind = "UserMessage", text = "Hello" },
                new { kind = "AssistantMessage", text = "Hi, how can I help?" },
                new { kind = "UserMessage", text = "Refund A-17" },
            },
            fixtures = TestAgent.Orders("A-17", "delivered"),
            conversationId = conversation,
        };

        var response = await factory
            .CreateClient()
            .PostAsJsonAsync(new Uri("/suwageval/turn", UriKind.Relative), turnRequest, TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var turn = Assert.Single(
            spans.Ended.ToArray(),
            span => span.OperationName == "turn" && span.GetTagItem("langfuse.session.id") as string == conversation
        );
        foreach (var scope in new[] { "observation", "trace" })
        {
            Assert.Equal("Refund A-17", turn.GetTagItem($"langfuse.{scope}.input"));
            Assert.Equal("Done.", turn.GetTagItem($"langfuse.{scope}.output"));
        }
    }

    [Fact]
    public void Tracing_is_off_without_the_keys_and_on_with_them()
    {
        using var chat = ScriptedChatClient.CallsOnce("refund_order", new() { ["order_id"] = "A-17", ["reason"] = "broken" }, "Done.");
        using var off = Factory("", null, null, chat);
        using var on = Factory("pk", null, null, chat);

        Assert.Null(off.Services.GetService<TracerProvider>());
        Assert.NotNull(on.Services.GetService<TracerProvider>());
    }

    [Fact]
    public void The_input_is_the_last_user_message_and_the_output_the_last_assistant_message_or_else_the_items()
    {
        var request = new TurnRequest
        {
            Conversation =
            [
                new UserMessage { Text = "first" },
                new AssistantMessage { Text = "answer" },
                new UserMessage { Text = "second" },
            ],
            Correlation = TurnCorrelation.None,
            ConversationId = null,
        };
        Assert.Equal("second", AgentTracing.InputText(request));
        Assert.Null(
            AgentTracing.InputText(
                new TurnRequest
                {
                    Conversation = [],
                    Correlation = TurnCorrelation.None,
                    ConversationId = null,
                }
            )
        );

        Assert.Equal(
            "last",
            AgentTracing.OutputText(
                new TurnResponse { Items = [new AssistantMessage { Text = "first" }, new AssistantMessage { Text = "last" }] }
            )
        );
        var noAssistant = AgentTracing.OutputText(
            new TurnResponse
            {
                Items = [new Event { Type = "state.changed", Data = System.Text.Json.JsonSerializer.SerializeToElement(1) }],
            }
        );
        Assert.Contains("state.changed", noAssistant, StringComparison.Ordinal);
    }

    [Fact]
    public void The_authorization_header_is_basic_auth_of_the_keys_and_missing_without_them()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["LANGFUSE_PUBLIC_KEY"] = "pk", ["LANGFUSE_SECRET_KEY"] = "sk" })
            .Build();
        Assert.Equal("Authorization=Basic cGs6c2s=", AgentTracing.AuthorizationHeader(configuration));
        Assert.Null(AgentTracing.AuthorizationHeader(new ConfigurationBuilder().Build()));
    }
}
