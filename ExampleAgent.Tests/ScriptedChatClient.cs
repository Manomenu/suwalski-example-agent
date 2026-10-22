using Microsoft.Extensions.AI;

namespace ExampleAgent.Tests;

/// <summary>A chat client that answers from a script instead of a model: the tests never call one.</summary>
internal sealed class ScriptedChatClient(Func<IReadOnlyList<ChatMessage>, ChatMessage> script) : IChatClient
{
    /// <summary>What the agent sent to the model on each call (history, tool results).</summary>
    public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

    /// <summary>The model asks for one tool call, then, once it has the result, says <paramref name="answer"/>.</summary>
    public static ScriptedChatClient CallsOnce(string tool, Dictionary<string, object?> arguments, string answer) =>
        new(messages =>
            messages[^1].Role == ChatRole.Tool
                ? new ChatMessage(ChatRole.Assistant, answer)
                : new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"call-{Guid.NewGuid():N}", tool, arguments)])
        );

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var sent = messages.ToList();
        Calls.Add(sent);
        return Task.FromResult(new ChatResponse(script(sent)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException("The agent runs without streaming.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
