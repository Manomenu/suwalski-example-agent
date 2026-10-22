using System.ClientModel;
using ExampleAgent;
using ExampleAgent.Shop;
using ExampleAgent.Support;
using Microsoft.Extensions.AI;
using OpenAI;
using Suwageval.AgentSdk.Invocation;

if (DotEnv.Find(Directory.GetCurrentDirectory()) is { } dotEnv)
{
    DotEnv.Load(dotEnv);
}
var builder = WebApplication.CreateBuilder(args);

// The settings are read when first needed (not here), so a test can supply them and replace the chat client.
builder.Services.AddSingleton(services => ModelSettings.From(services.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<IChatClient>(services =>
{
    var model = services.GetRequiredService<ModelSettings>();
    var client = new OpenAIClient(new ApiKeyCredential(model.ApiKey), new OpenAIClientOptions { Endpoint = new Uri(model.BaseUrl) });
    return client.GetChatClient(model.Model).AsIChatClient();
});
builder.Services.AddSingleton(PromptSettings.From(builder.Configuration));
builder.Services.AddSingleton<ShopStore>();
builder.Services.AddSingleton<IAgentContractSource, SupportContractSource>();
builder.Services.AddSingleton<ITurnHandler, SupportTurnHandler>();

var app = builder.Build();
_ = app.Services.GetRequiredService<PromptSettings>(); // a bad SUWEXAG_PROMPT stops the agent at start
app.MapSuwagevalAgent(); // GET /suwageval/contract, POST /suwageval/turn
app.Run();

/// <summary>The entry point, visible to the tests that run the agent in process.</summary>
internal sealed partial class Program;
