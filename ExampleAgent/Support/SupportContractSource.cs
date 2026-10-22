using System.Text.Json.Nodes;
using ExampleAgent.Policy;
using ExampleAgent.Shop;
using Suwageval.AgentSdk.Agents;
using Suwageval.AgentSdk.Invocation;

namespace ExampleAgent.Support;

/// <summary>
/// What this agent tells Suwageval about itself (<c>GET /suwageval/contract</c>), built from its real definition: the prompt,
/// the model's name, the tools' schemas as the model gets them. No model call. Change any of it and the next run is a new version.
/// </summary>
internal sealed class SupportContractSource(ModelSettings model, IConfiguration configuration) : IAgentContractSource
{
    /// <summary>The event emitted after a successful refund; its <c>data</c> follows <see cref="StateChangedSchema"/>.</summary>
    public const string StateChanged = "state.changed";

    public static System.Text.Json.JsonElement StateChangedSchema { get; } =
        ContractSchema.ObjectSchema(
            new JsonObject
            {
                ["node"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("refunded") },
                ["orderId"] = new JsonObject { ["type"] = "string" },
            },
            "node",
            "orderId"
        );

    public Task<AgentContract> GetContractAsync(CancellationToken cancellationToken) =>
        Task.FromResult(
            new AgentContract
            {
                Label = configuration["SUWAGEVAL_LABEL"] is { Length: > 0 } label ? label : null,
                Agents =
                [
                    new LlmAgentDefinition
                    {
                        Name = SupportAgent.Name,
                        Model = new ModelDefinition { Name = model.Model },
                        Config = SupportAgent.SystemPrompt,
                        Tools =
                        [
                            .. new ShopTools(Shop.Shop.From(null))
                                .ToFunctions()
                                .Select(function => new ToolDefinition
                                {
                                    Name = function.Name,
                                    Description = function.Description,
                                    Parameters = function.JsonSchema,
                                }),
                        ],
                    },
                ],
                Context = new ContextDefinition
                {
                    UsesRetrieval = true,
                    Retrieval = new RetrievalDefinition { DataVersion = PolicyIndex.DataVersion, Description = PolicyIndex.Description },
                    Events = [new EventDeclaration { Type = StateChanged, DataSchema = StateChangedSchema }],
                    FixturesSchema = ShopFixtures.Schema,
                },
            }
        );
}
