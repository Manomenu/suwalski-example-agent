namespace ExampleAgent.Shop;

/// <summary>The orders one conversation works on, in memory.</summary>
internal sealed class Shop
{
    private readonly Dictionary<string, Order> orders;

    private Shop(Dictionary<string, Order> orders) => this.orders = orders;

    /// <summary>A shop holding fresh copies of the fixtures' orders (none without fixtures).</summary>
    public static Shop From(ShopFixtures? fixtures) =>
        new(
            (fixtures?.Orders ?? new Dictionary<string, OrderFixture>()).ToDictionary(
                order => order.Key,
                order => new Order(order.Value.Status, order.Value.Items, order.Value.Total)
            )
        );

    public Order? Find(string orderId) => orders.GetValueOrDefault(orderId);
}
