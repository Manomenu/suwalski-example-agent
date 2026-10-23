using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExampleAgent.Policy;
using ExampleAgent.Tracing;
using Microsoft.Extensions.AI;

namespace ExampleAgent.Shop;

/// <summary>The tools of the support agent over one shop. Parameter names are the tools' own (<c>order_id</c>): the scenarios use them.</summary>
internal sealed class ShopTools(Shop shop)
{
    public const string GetOrderName = "get_order";
    public const string RefundOrderName = "refund_order";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The tools as the model sees them: the two order tools over this shop, and the policy lookup.</summary>
    public IReadOnlyList<AIFunction> ToFunctions() =>
        [
            AIFunctionFactory.Create(GetOrder, GetOrderName, serializerOptions: Json),
            AIFunctionFactory.Create(RefundOrder, RefundOrderName, serializerOptions: Json),
            AIFunctionFactory.Create(PolicyIndex.SearchPolicy, PolicyIndex.ToolName, serializerOptions: Json),
        ];

    [Description("Look up an order: its status, items and total.")]
    public object GetOrder([Description("Order id, e.g. A-17")] string order_id) =>
        shop.Find(order_id) is { } order
            ? new
            {
                orderId = order_id,
                status = order.Status,
                items = order.Items,
                total = order.Total,
            }
            : NotFound(order_id);

    [Description("Refund an order. Only a delivered order can be refunded.")]
    public object RefundOrder(
        [Description("Order id, e.g. A-17")] string order_id,
        [Description("Why the customer wants the refund")] string reason
    )
    {
        if (shop.Find(order_id) is not { } order)
        {
            return NotFound(order_id);
        }
        if (order.Status != OrderStatus.delivered)
        {
            var refusal = $"Order {order_id} is {order.Status}; only a delivered order can be refunded.";
            // The tool's own span (M.E.AI's execute_tool, the current activity here): a refused action is a warning, not a failure.
            AgentTracing.Flag(Activity.Current, AgentTracing.Warning, refusal);
            return new { error = refusal };
        }
        order.Status = OrderStatus.refunded;
        return new
        {
            orderId = order_id,
            refunded = order.Total,
            reason,
        };
    }

    private static object NotFound(string orderId)
    {
        var message = $"Order {orderId} not found.";
        AgentTracing.Flag(Activity.Current, AgentTracing.Error, message);
        return new { error = message };
    }
}
