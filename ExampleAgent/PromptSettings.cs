using ExampleAgent.Support;

namespace ExampleAgent;

/// <summary>The system prompt the agent runs with: <c>SUWEXAG_PROMPT</c> is <c>default</c> (unset too) or <c>concise</c>.</summary>
internal sealed record PromptSettings(string Text)
{
    public static PromptSettings From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration["SUWEXAG_PROMPT"] switch
        {
            null or "" or "default" => new PromptSettings(SupportAgent.DefaultPrompt),
            "concise" => new PromptSettings(SupportAgent.ConcisePrompt),
            var other => throw new InvalidOperationException($"SUWEXAG_PROMPT is \"{other}\": use \"default\" or \"concise\"."),
        };
    }
}
