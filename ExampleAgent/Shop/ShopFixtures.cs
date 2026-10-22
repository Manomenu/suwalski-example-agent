using System.Text.Json.Nodes;
using Suwageval.AgentSdk.Agents;

namespace ExampleAgent.Shop;

/// <summary>An order as a scenario's <c>fixtures</c> write it.</summary>
internal sealed record OrderFixture(OrderStatus Status, IReadOnlyList<string> Items, decimal Total);

/// <summary>
/// The test data a scenario starts the shop with: the shop has no orders of its own, so a scenario file shows every order
/// the agent can find. <see cref="Schema"/> is declared in the contract (<c>context.fixturesSchema</c>).
/// </summary>
internal sealed record ShopFixtures
{
    public IReadOnlyDictionary<string, OrderFixture> Orders { get; init; } = new Dictionary<string, OrderFixture>();

    public static System.Text.Json.JsonElement Schema { get; } =
        ContractSchema.ObjectSchema(
            new JsonObject
            {
                ["orders"] = new JsonObject
                {
                    ["type"] = "object",
                    ["description"] = "The shop's orders, by order id, e.g. A-17",
                    ["additionalProperties"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["status"] = ContractSchema.EnumSchema<OrderStatus>(),
                            ["items"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject { ["type"] = "string" },
                            },
                            ["total"] = new JsonObject { ["type"] = "number" },
                        },
                        ["required"] = new JsonArray("status", "items", "total"),
                        ["additionalProperties"] = false,
                    },
                },
            }
        );
}
