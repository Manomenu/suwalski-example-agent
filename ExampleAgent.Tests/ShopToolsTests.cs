using System.Text.Json;
using ExampleAgent.Shop;

namespace ExampleAgent.Tests;

public class ShopToolsTests
{
    private static ShopTools ToolsFor(string orderId, OrderStatus status) =>
        new(
            Shop.Shop.From(
                new ShopFixtures { Orders = new Dictionary<string, OrderFixture> { [orderId] = new(status, ["Kettle"], 89.9m) } }
            )
        );

    private static string Json(object result) => JsonSerializer.Serialize(result);

    [Fact]
    public void An_order_is_looked_up_with_its_status_items_and_total()
    {
        var result = Json(ToolsFor("A-17", OrderStatus.shipped).GetOrder("A-17"));

        Assert.Contains("\"orderId\":\"A-17\"", result, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"shipped\"", result, StringComparison.Ordinal);
        Assert.Contains("Kettle", result, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_order_is_an_error()
    {
        Assert.Contains("not found", Json(ToolsFor("A-17", OrderStatus.delivered).GetOrder("Z-99")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_delivered_order_is_refunded_once()
    {
        var tools = ToolsFor("A-17", OrderStatus.delivered);

        Assert.Contains("\"refunded\":89.9", Json(tools.RefundOrder("A-17", "broken")), StringComparison.Ordinal);
        Assert.Contains("only a delivered order", Json(tools.RefundOrder("A-17", "again")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("processing")]
    [InlineData("shipped")]
    [InlineData("refunded")]
    public void An_order_that_is_not_delivered_is_not_refunded(string status)
    {
        Assert.Contains(
            "only a delivered order",
            Json(ToolsFor("A-17", Enum.Parse<OrderStatus>(status)).RefundOrder("A-17", "x")),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void The_tools_the_model_gets_keep_their_parameter_names()
    {
        var schemas = ToolsFor("A-17", OrderStatus.delivered).ToFunctions().ToDictionary(f => f.Name, f => f.JsonSchema.ToString());

        Assert.Equal(["get_order", "refund_order", "search_policy"], schemas.Keys.Order());
        Assert.Contains("order_id", schemas["refund_order"], StringComparison.Ordinal);
    }
}
