using System.Net.Http.Json;
using ExampleAgent.Tracing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

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
        Assert.All(trace, span => Assert.Equal(conversation, span.GetTagItem("langfuse.session.id")));
        Assert.DoesNotContain(snapshot, span => span.OperationName.Contains("HttpRequestIn", StringComparison.Ordinal));
    }
}
