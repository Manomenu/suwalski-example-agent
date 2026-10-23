using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Suwageval.AgentSdk;
using Suwageval.AgentSdk.Conversations;
using Suwageval.AgentSdk.Invocation;

namespace ExampleAgent.Tracing;

/// <summary>
/// Traces to Langfuse over OTLP/HTTP: the agent's own source <c>Suwexag</c> only (one <c>turn</c> root per turn, a generation per model
/// call, a tool per tool call), never the HTTP layer: no ASP.NET Core or HttpClient instrumentation is added.
/// </summary>
internal static class AgentTracing
{
    public const string SourceName = "Suwexag";
    public const string TurnName = "turn";

    // Not a Suwageval key: the conversation id travels as baggage on the turn so the processor can copy it onto every span.
    public const string ConversationBaggage = "suwexag.conversation.id";

    public const string LevelTag = "langfuse.observation.level";
    public const string Error = "ERROR";
    public const string Warning = "WARNING";

    public static readonly ActivitySource Source = new(SourceName);

    public static bool IsOn(IConfiguration configuration) =>
        AuthorizationHeader(configuration) is not null && !string.IsNullOrEmpty(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

    /// <summary>The OTLP <c>Authorization</c> header for Langfuse, or null when a key is missing (tracing is then off).</summary>
    public static string? AuthorizationHeader(IConfiguration configuration)
    {
        var publicKey = configuration["LANGFUSE_PUBLIC_KEY"];
        var secretKey = configuration["LANGFUSE_SECRET_KEY"];
        return string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(secretKey)
            ? null
            : "Authorization=Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{publicKey}:{secretKey}"));
    }

    /// <summary>
    /// Adds the tracer provider. What it listens to and exports is decided when it is built (the configuration is final by then):
    /// without Langfuse's keys it listens to nothing, so tracing costs nothing.
    /// </summary>
    /// <summary>The service name the traces carry (otherwise OpenTelemetry says <c>unknown_service:dotnet</c>).</summary>
    public const string ServiceName = "suwexag";

    public static void AddAgentTracing(this IServiceCollection services)
    {
        services.AddOpenTelemetry().WithTracing(_ => { });
        services.ConfigureOpenTelemetryTracerProvider(
            (provider, builder) =>
            {
                var configuration = provider.GetRequiredService<IConfiguration>();
                if (
                    AuthorizationHeader(configuration) is not { } header
                    || configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is not { Length: > 0 } endpoint
                )
                {
                    return;
                }
                // The endpoint is read from the configuration (appsettings.json); Langfuse takes OTLP over HTTP/protobuf, not gRPC.
                var options = new OtlpExporterOptions
                {
                    Endpoint = new Uri($"{endpoint.TrimEnd('/')}/v1/traces"),
                    Protocol = OtlpExportProtocol.HttpProtobuf,
                    Headers = header,
                };
                builder
                    .ConfigureResource(resource => resource.AddService(ServiceName))
                    .AddSource(SourceName)
                    .AddProcessor(
                        new SuwagevalTraceProcessor(
                            configuration["SUWAGEVAL_LABEL"] is { Length: > 0 } label ? label : null,
                            Release(configuration)
                        )
                    )
                    .AddProcessor(new BatchActivityExportProcessor(new OtlpTraceExporter(options)));
            }
        );
    }

    /// <summary>
    /// The release (<c>langfuse.release</c>): <c>SUWEXAG_RELEASE</c> when set (an image has no .git), else the git commit the build
    /// recorded in the assembly's informational version (<c>SourceRevisionId</c>, set by the csproj from git); null when neither is known.
    /// </summary>
    public static string? Release(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration["SUWEXAG_RELEASE"] is { Length: > 0 } release)
        {
            return release;
        }
        var informational = typeof(AgentTracing)
            .Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion;
        return informational?.Split('+', 2) is [_, { Length: > 0 } revision] ? revision : null;
    }

    /// <summary>Marks an observation <c>WARNING</c> or <c>ERROR</c> with a message (<c>langfuse.observation.level</c>, <c>status_message</c>).</summary>
    public static void Flag(Activity? span, string level, string? message)
    {
        span?.SetTag(LevelTag, level);
        if (!string.IsNullOrEmpty(message))
        {
            span?.SetTag("langfuse.observation.status_message", message);
        }
    }

    /// <summary>The turn's input: the conversation the agent got. On the observation and on the trace (the trace list shows it).</summary>
    public static void RecordInput(Activity? turn, TurnRequest request)
    {
        if (turn is null)
        {
            return;
        }
        var json = JsonSerializer.Serialize(request.Conversation, ContractJson.Options);
        turn.SetTag("langfuse.observation.input", json);
        turn.SetTag("langfuse.trace.input", json);
    }

    /// <summary>
    /// The turn's output: what the agent answered (the response's items). Each event it emitted also becomes an <c>event</c>
    /// observation under the turn, named after the event's type, with the event's data as input and output.
    /// </summary>
    public static void RecordOutput(Activity? turn, TurnResponse response)
    {
        if (turn is null)
        {
            return;
        }
        var json = JsonSerializer.Serialize(response.Items, ContractJson.Options);
        turn.SetTag("langfuse.observation.output", json);
        turn.SetTag("langfuse.trace.output", json);
        Activity.Current = turn; // the events are children of the turn and inherit its baggage (a parent context would not carry it)
        foreach (var item in response.Items.OfType<Event>())
        {
            using var observation = Source.StartActivity(item.Type);
            var data = item.Data.GetRawText();
            observation?.SetTag("langfuse.observation.type", "event");
            observation?.SetTag("langfuse.observation.input", data);
            observation?.SetTag("langfuse.observation.output", data);
        }
    }

    /// <summary>Starts the <c>turn</c> span as the root of a new trace (no parent); null when nobody listens.</summary>
    public static Activity? StartTurn(TurnRequest request)
    {
        // A default parent context would still adopt Activity.Current (ASP.NET's request activity): let go of it to get a true root.
        Activity.Current = null;
        var turn = Source.StartActivity(TurnName, ActivityKind.Internal, parentContext: default);
        if (turn is null)
        {
            return null;
        }
        var correlation = request.Correlation;
        SetBaggage(turn, SuwagevalBaggage.RunId, correlation.RunId);
        SetBaggage(turn, SuwagevalBaggage.ScenarioKey, correlation.Scenario);
        SetBaggage(turn, SuwagevalBaggage.ScenarioTypeKey, correlation.ScenarioType?.ToString());
        SetBaggage(turn, SuwagevalBaggage.ScenarioSample, correlation.Sample);
        SetBaggage(turn, SuwagevalBaggage.ScenarioTags, string.Join(',', correlation.ScenarioTags));
        SetBaggage(turn, SuwagevalBaggage.RunTags, string.Join(',', correlation.RunTags));
        SetBaggage(turn, SuwagevalBaggage.RunGroup, correlation.RunGroup);
        SetBaggage(turn, SuwagevalBaggage.AgentVersionId, correlation.AgentVersionId);
        SetBaggage(turn, ConversationBaggage, request.ConversationId);
        SuwagevalTraceProcessor.Apply(turn); // the processor saw the span before its baggage was set
        return turn;
    }

    private static void SetBaggage(Activity turn, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            turn.SetBaggage(key, value);
        }
    }
}
