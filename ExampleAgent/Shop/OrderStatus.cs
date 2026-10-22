using System.Text.Json.Serialization;

namespace ExampleAgent.Shop;

/// <summary>The member names are the wire format (fixtures, tool results, the contract's schema): lower case on purpose.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OrderStatus>))]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "IDE1006:Naming Styles", Justification = "Names are the wire format.")]
internal enum OrderStatus
{
    processing,
    shipped,
    delivered,
    refunded,
}
