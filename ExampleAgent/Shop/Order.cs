namespace ExampleAgent.Shop;

/// <summary>An order in the shop's memory; a refund changes its status.</summary>
internal sealed class Order(OrderStatus status, IReadOnlyList<string> items, decimal total)
{
    public OrderStatus Status { get; set; } = status;

    public IReadOnlyList<string> Items { get; } = items;

    public decimal Total { get; } = total;
}
