using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Suwageval.AgentSdk;
using Suwageval.AgentSdk.Conversations;
using Suwageval.AgentSdk.Invocation;

namespace ExampleAgent.Tests;

/// <summary>The agent running in process, over a chat client the test scripts.</summary>
internal sealed class TestAgent : IDisposable
{
    private readonly WebApplicationFactory<Program> factory;

    private readonly IChatClient chatClient;

    public TestAgent(IChatClient chatClient)
    {
        this.chatClient = chatClient;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("OPENAI_BASE_URL", "http://localhost:1/v1");
            host.UseSetting("OPENAI_API_KEY", "not-a-key");
            host.UseSetting("OPENAI_MODEL", "test-model");
            host.UseSetting("LANGFUSE_PUBLIC_KEY", ""); // a developer's .env must not make the tests export traces
            host.UseSetting("LANGFUSE_SECRET_KEY", "");
            host.ConfigureServices(services => services.AddSingleton(chatClient));
        });
    }

    public HttpClient Client => factory.CreateClient();

    /// <summary>Posts a turn; <paramref name="turn"/> is the request as JSON, the way Suwageval sends it.</summary>
    public async Task<HttpResponseMessage> PostAsync(object turn) => await Client.PostAsJsonAsync("/suwageval/turn", turn);

    public static async Task<IReadOnlyList<ConversationItem>> ItemsOf(HttpResponseMessage response)
    {
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var turn = await response.Content.ReadFromJsonAsync<TurnResponse>(ContractJson.Options);
        return turn!.Items;
    }

    public static object UserSays(string text, object? fixtures = null, string? conversationId = null) =>
        new
        {
            conversation = new object[] { new { kind = "UserMessage", text } },
            fixtures,
            conversationId,
        };

    public static JsonElement Orders(string orderId, string status, decimal total = 89.9m) =>
        JsonSerializer.SerializeToElement(
            new
            {
                orders = new Dictionary<string, object>
                {
                    [orderId] = new
                    {
                        status,
                        items = Kettle,
                        total,
                    },
                },
            }
        );

    private static readonly string[] Kettle = ["Kettle"];

    public void Dispose()
    {
        factory.Dispose();
        chatClient.Dispose();
    }
}
