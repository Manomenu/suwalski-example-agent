"""The shop's tools: plain functions over a few fake orders held in memory."""

from typing import Annotated

from agent_framework import tool
from pydantic import Field

ORDERS: dict[str, dict] = {}


def reset_orders() -> None:
    """Put the fake shop back to its starting state (tests call this)."""
    ORDERS.clear()
    ORDERS.update(
        {
            "A-17": {"status": "delivered", "items": ["Kettle", "Mug x2"], "total": 89.90},
            "A-18": {"status": "shipped", "items": ["Backpack"], "total": 149.00},
            "A-19": {"status": "processing", "items": ["Desk lamp"], "total": 59.50},
            "A-20": {"status": "refunded", "items": ["Headphones"], "total": 199.00},
        }
    )


reset_orders()


@tool
def get_order(order_id: Annotated[str, Field(description="Order id, e.g. A-17")]) -> dict:
    """Look up an order: its status, items and total."""
    order = ORDERS.get(order_id)
    if order is None:
        return {"error": f"Order {order_id} not found."}
    return {"orderId": order_id, **order}


@tool
def refund_order(
    order_id: Annotated[str, Field(description="Order id, e.g. A-17")],
    reason: Annotated[str, Field(description="Why the customer wants the refund")],
) -> dict:
    """Refund an order. Only a delivered order can be refunded."""
    order = ORDERS.get(order_id)
    if order is None:
        return {"error": f"Order {order_id} not found."}
    if order["status"] != "delivered":
        return {"error": f"Order {order_id} is {order['status']}; only a delivered order can be refunded."}
    order["status"] = "refunded"
    return {"orderId": order_id, "refunded": order["total"], "reason": reason}
