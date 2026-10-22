namespace ExampleAgent;

/// <summary>The model the agent talks to: any OpenAI-compatible endpoint, from the environment (or <c>.env</c>).</summary>
internal sealed record ModelSettings(string BaseUrl, string ApiKey, string Model)
{
    public static ModelSettings From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new ModelSettings(
            Required(configuration, "OPENAI_BASE_URL"),
            Required(configuration, "OPENAI_API_KEY"),
            Required(configuration, "OPENAI_MODEL")
        );
    }

    private static string Required(IConfiguration configuration, string name) =>
        configuration[name] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} is not set (see .env.example).");
}
