namespace ExampleAgent.Tracing;

internal static partial class TracingLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Tracing to Langfuse is on")]
    public static partial void On(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Tracing is off: LANGFUSE_PUBLIC_KEY / LANGFUSE_SECRET_KEY are not set (see .env.example)"
    )]
    public static partial void Off(ILogger logger);
}
