using System.Diagnostics;
using OpenTelemetry;
using Suwageval.AgentSdk.Invocation;

namespace ExampleAgent.Tracing;

/// <summary>
/// Copies what Suwageval sent (the baggage on the turn span, inherited by its children) onto every span of the agent's source as
/// Langfuse trace attributes: Langfuse filters only see a trace-level value that is present on every span. Names:
/// https://langfuse.com/integrations/native/opentelemetry
/// </summary>
internal sealed class SuwagevalTraceProcessor : BaseProcessor<Activity>
{
    public const string Environment = "suwageval";

    public override void OnStart(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Apply(data);
    }

    /// <summary>Sets the trace attributes on a span of the agent's source from its (inherited) baggage; safe to call twice.</summary>
    public static void Apply(Activity data)
    {
        if (data.Source.Name != AgentTracing.SourceName)
        {
            return;
        }
        if (data.GetBaggageItem(AgentTracing.ConversationBaggage) is { } conversation)
        {
            data.SetTag("langfuse.session.id", conversation);
        }

        var runId = data.GetBaggageItem(SuwagevalBaggage.RunId);
        var scenario = data.GetBaggageItem(SuwagevalBaggage.ScenarioKey);
        var scenarioType = data.GetBaggageItem(SuwagevalBaggage.ScenarioTypeKey);
        var sample = data.GetBaggageItem(SuwagevalBaggage.ScenarioSample);
        var runGroup = data.GetBaggageItem(SuwagevalBaggage.RunGroup);
        var scenarioTags = Split(data.GetBaggageItem(SuwagevalBaggage.ScenarioTags));
        var runTags = Split(data.GetBaggageItem(SuwagevalBaggage.RunTags));

        var fromSuwageval = runId is not null || scenario is not null || sample is not null || runGroup is not null;
        if (!fromSuwageval)
        {
            return; // no Suwageval: the default environment, no suwageval.* values
        }
        data.SetTag("langfuse.environment", Environment);
        // The metadata keys are the baggage keys Suwageval sends (suwageval.run.id, …): one name for a value everywhere.
        SetMetadata(data, SuwagevalBaggage.RunId, runId);
        SetMetadata(data, SuwagevalBaggage.ScenarioKey, scenario);
        SetMetadata(data, SuwagevalBaggage.ScenarioSample, sample);
        SetMetadata(data, SuwagevalBaggage.ScenarioTypeKey, scenarioType);
        SetMetadata(data, SuwagevalBaggage.RunGroup, runGroup);
        SetMetadata(data, SuwagevalBaggage.RunTags, runTags.Length > 0 ? string.Join(",", runTags) : null);

        SetMetadata(data, SuwagevalBaggage.ScenarioTags, scenarioTags.Length > 0 ? string.Join(",", scenarioTags) : null);
        // No Langfuse tags at all: every value here can be new on any run (a run id, a group, a scenario tag per task), and each
        // would stay in Langfuse's tag list for good. Metadata filters just as well.
    }

    /// <summary>Langfuse's observation type for what M.E.AI emits: a model call is a generation, a tool call a tool.</summary>
    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Source.Name != AgentTracing.SourceName)
        {
            return;
        }
        var type = data.GetTagItem("gen_ai.operation.name") switch
        {
            "chat" => "generation",
            "execute_tool" => "tool",
            _ => null,
        };
        if (type is not null)
        {
            data.SetTag("langfuse.observation.type", type);
        }
    }

    private static void SetMetadata(Activity activity, string key, string? value)
    {
        if (value is not null)
        {
            activity.SetTag($"langfuse.trace.metadata.{key}", value);
        }
    }

    private static string[] Split(string? list) => list?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
}
