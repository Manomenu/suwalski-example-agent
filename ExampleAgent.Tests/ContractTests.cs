using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Suwageval.AgentSdk;
using Suwageval.AgentSdk.Agents;

namespace ExampleAgent.Tests;

public class ContractTests
{
    private static async Task<AgentContract> ServedContractAsync()
    {
        using var agent = new TestAgent(new ScriptedChatClient(_ => new ChatMessage(ChatRole.Assistant, "unused")));
        return (await agent.Client.GetFromJsonAsync<AgentContract>("/suwageval/contract", ContractJson.Options))!;
    }

    [Fact]
    public async Task The_contract_passes_the_rules_the_server_applies()
    {
        Assert.Empty(AgentContractRules.Problems(await ServedContractAsync()));
    }

    [Fact]
    public async Task The_contract_describes_the_support_agent_its_tools_events_retrieval_and_fixtures()
    {
        var contract = await ServedContractAsync();

        var agent = Assert.Single(contract.Agents);
        Assert.Equal("support", agent.Name);
        Assert.Equal("test-model", agent.Model.Name);
        Assert.Equal(["get_order", "refund_order", "search_policy"], agent.Tools.Select(t => t.Name).Order());
        Assert.True(contract.Context.UsesRetrieval);
        Assert.Equal("state.changed", Assert.Single(contract.Context.Events).Type);
        Assert.NotNull(contract.Context.FixturesSchema);
    }
}
