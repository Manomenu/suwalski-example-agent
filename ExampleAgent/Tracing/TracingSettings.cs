namespace ExampleAgent.Tracing;

/// <summary>
/// <c>Tracing:SensitiveData</c>: whether spans carry message contents, tool arguments and results, and the turn's input and output.
/// Off in appsettings.json (a real app's conversations are personal data); this repository's local use turns it on in <c>.env</c>.
/// </summary>
internal sealed record TracingSettings(bool SensitiveData)
{
    public static TracingSettings From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new TracingSettings(configuration.GetValue<bool>("Tracing:SensitiveData"));
    }
}
