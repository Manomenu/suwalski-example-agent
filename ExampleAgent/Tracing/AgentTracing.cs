using System.Diagnostics;
using System.Text;
using OpenTelemetry.Resources;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;
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
                    .AddProcessor(new SuwagevalTraceProcessor())
                    .AddProcessor(new BatchActivityExportProcessor(new OtlpTraceExporter(options)));
            }
        );
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
