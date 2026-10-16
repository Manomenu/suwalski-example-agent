"""The shop's tools: plain functions over a few fake orders held in memory."""

import copy
from contextvars import ContextVar
from typing import Annotated

from agent_framework import tool
from pydantic import Field

# The shop every turn starts from. A turn works on its own copy (start_turn), so turns that run
# in parallel — or one run after another — never see each other's refunds: an evaluation must
# not depend on what an earlier one did.
STARTING_ORDERS: dict[str, dict] = {
    "A-17": {"status": "delivered", "items": ["Kettle", "Mug x2"], "total": 89.90},
    "A-18": {"status": "shipped", "items": ["Backpack"], "total": 149.00},
    "A-19": {"status": "processing", "items": ["Desk lamp"], "total": 59.50},
    "A-20": {"status": "refunded", "items": ["Headphones"], "total": 199.00},
}

_orders: ContextVar[dict[str, dict]] = ContextVar("orders")


def start_turn() -> dict[str, dict]:
    """Give the current turn a fresh copy of the shop; returns it (tests read it)."""
    orders = copy.deepcopy(STARTING_ORDERS)
    _orders.set(orders)
    return orders


def _shop() -> dict[str, dict]:
    try:
        return _orders.get()
    except LookupError:
        return start_turn()


@tool
def get_order(order_id: Annotated[str, Field(description="Order id, e.g. A-17")]) -> dict:
    """Look up an order: its status, items and total."""
    order = _shop().get(order_id)
    if order is None:
        return {"error": f"Order {order_id} not found."}
    return {"orderId": order_id, **order}


@tool
def refund_order(
    order_id: Annotated[str, Field(description="Order id, e.g. A-17")],
    reason: Annotated[str, Field(description="Why the customer wants the refund")],
) -> dict:
    """Refund an order. Only a delivered order can be refunded."""
    order = _shop().get(order_id)
    if order is None:
        return {"error": f"Order {order_id} not found."}
    if order["status"] != "delivered":
        return {"error": f"Order {order_id} is {order['status']}; only a delivered order can be refunded."}
    order["status"] = "refunded"
    return {"orderId": order_id, "refunded": order["total"], "reason": reason}
