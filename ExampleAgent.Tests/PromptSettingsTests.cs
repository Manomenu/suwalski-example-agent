using ExampleAgent.Support;
using Microsoft.Extensions.Configuration;

namespace ExampleAgent.Tests;

public class PromptSettingsTests
{
    private static PromptSettings From(string? value) =>
        PromptSettings.From(new ConfigurationBuilder().AddInMemoryCollection(value is null ? [] : [new("SUWEXAG_PROMPT", value)]).Build());

    [Fact]
    public void Unset_or_default_is_the_default_prompt()
    {
        Assert.Equal(SupportAgent.DefaultPrompt, From(null).Text);
        Assert.Equal(SupportAgent.DefaultPrompt, From("default").Text);
    }

    [Fact]
    public void Concise_is_the_concise_prompt() => Assert.Equal(SupportAgent.ConcisePrompt, From("concise").Text);

    [Fact]
    public void Anything_else_fails_with_the_allowed_values()
    {
        var error = Assert.Throws<InvalidOperationException>(() => From("verbose"));
        Assert.Contains("\"default\" or \"concise\"", error.Message, StringComparison.Ordinal);
    }
}
