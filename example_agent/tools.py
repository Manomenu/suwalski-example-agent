"""The shop's tools: plain functions over a few fake orders held in memory."""

import copy
from contextvars import ContextVar
from typing import Annotated

from agent_framework import tool
from pydantic import Field

# The shop a turn works on. It holds only what the scenario's fixtures put in it (no order is built
# in): the scenario file shows every order the agent can find. A turn works on its own copy, so
# turns that run in parallel — or one run after another — never see each other's refunds.
_orders: ContextVar[dict[str, dict]] = ContextVar("orders")


def start_turn(orders: dict[str, dict]) -> dict[str, dict]:
    """Give the current turn its own copy of these orders; returns it (tests read it)."""
    shop = copy.deepcopy(orders)
    _orders.set(shop)
    return shop


def _shop() -> dict[str, dict]:
    try:
        return _orders.get()
    except LookupError:
        return start_turn({})


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
