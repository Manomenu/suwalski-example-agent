namespace ExampleAgent.Chat;

/// <summary>The agent's answer to the customer, with the ids of the policy paragraphs it cited (none when it cited none).</summary>
internal sealed record ChatResponse(string Answer, IReadOnlyList<string> Sources);
