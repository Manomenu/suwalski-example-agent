using ExampleAgent.Policy;

namespace ExampleAgent.Tests;

public class PolicyIndexTests
{
    [Fact]
    public void A_query_finds_the_paragraph_sharing_its_words()
    {
        var best = PolicyIndex.Search("can I refund a shipped order")[0];

        Assert.Equal("refund-policy", best.Chunk.Source);
        Assert.True(best.Score > 0);
    }

    [Fact]
    public void A_query_sharing_no_word_finds_nothing() => Assert.Empty(PolicyIndex.Search("zebra"));
}
