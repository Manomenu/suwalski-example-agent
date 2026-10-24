using System.ClientModel;
using System.Diagnostics;
using ExampleAgent;
using ExampleAgent.Chat;
using ExampleAgent.Shop;
using ExampleAgent.Support;
using ExampleAgent.Tracing;
using Microsoft.Extensions.AI;
using OpenAI;
using Suwageval.AgentSdk.Invocation;
using Suwageval.AgentSdk.Monitoring;

if (DotEnv.Find(Directory.GetCurrentDirectory()) is { } dotEnv)
{
    DotEnv.Load(dotEnv);
}

// Nothing leaves the process in a header: with the default propagator the HttpClient behind the model client would forward
// traceparent and baggage (Suwageval's run, scenario...) to the model provider.
DistributedContextPropagator.Current = DistributedContextPropagator.CreateNoOutputPropagator();
var builder = WebApplication.CreateBuilder(args);
builder.AddAgentTracing();
builder.Services.AddSingleton(TracingSettings.From(builder.Configuration));

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

// The shop's real chat, and its turns to Suwageval's monitoring when Suwageval:Url is set (.env: Suwageval__Url, __Agent, __Environment).
builder.Services.AddSingleton<ChatHistory>();
builder.Services.AddSuwagevalMonitoring(builder.Configuration);

var app = builder.Build();
if (AgentTracing.IsOn(builder.Configuration))
{
    TracingLog.On(app.Logger);
}
else
{
    TracingLog.Off(app.Logger);
}
_ = app.Services.GetRequiredService<PromptSettings>(); // a bad SUWEXAG_PROMPT stops the agent at start
app.MapSuwagevalAgent(); // GET /suwageval/contract, POST /suwageval/turn
app.MapChat(); // POST /chat: the customers' chat, monitored
app.Run();

/// <summary>The entry point, visible to the tests that run the agent in process.</summary>
internal sealed partial class Program;
