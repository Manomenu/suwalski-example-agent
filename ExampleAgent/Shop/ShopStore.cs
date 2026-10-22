using System.Collections.Concurrent;

namespace ExampleAgent.Shop;

/// <summary>
/// Which shop a turn works on. Suwageval runs scenarios in parallel and one run after another, so a conversation must not see
/// another one's refunds: without a <c>conversationId</c> every turn starts from a fresh copy of the fixtures. A simulated
/// conversation sends the same id with every turn: its first turn starts fresh, later turns go on from what earlier ones left
/// (an order refunded in turn 2 is refunded in turn 3). Kept in memory and never evicted — fine for an example, not for production.
/// </summary>
internal sealed class ShopStore
{
    private readonly ConcurrentDictionary<string, Shop> conversations = new();

    public Shop For(string? conversationId, ShopFixtures? fixtures) =>
        conversationId is null ? Shop.From(fixtures) : conversations.GetOrAdd(conversationId, _ => Shop.From(fixtures));
}
