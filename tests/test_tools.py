import pytest

from example_agent.tools import get_order, refund_order, start_turn

ORDERS = {
    "A-17": {"status": "delivered", "items": ["Kettle"], "total": 89.90},
    "A-18": {"status": "shipped", "items": ["Backpack"], "total": 149.00},
}


@pytest.fixture(autouse=True)
def shop():
    return start_turn(ORDERS)


def test_get_order_found():
    result = get_order.func("A-17")
    assert result["status"] == "delivered"
    assert result["total"] == 89.90


def test_get_order_unknown():
    assert "not found" in get_order.func("Z-1")["error"]


def test_refund_delivered_order(shop):
    result = refund_order.func("A-17", "broken kettle")
    assert result["orderId"] == "A-17"
    assert shop["A-17"]["status"] == "refunded"


def test_refund_shipped_order_is_refused(shop):
    result = refund_order.func("A-18", "changed my mind")
    assert "only a delivered order" in result["error"]
    assert shop["A-18"]["status"] == "shipped"


def test_refund_twice_is_refused():
    refund_order.func("A-17", "x")
    assert "error" in refund_order.func("A-17", "x")


def test_every_turn_starts_from_its_own_copy_of_the_orders():
    refund_order.func("A-17", "broken kettle")

    fresh = start_turn(ORDERS)

    assert fresh["A-17"]["status"] == "delivered"
    assert ORDERS["A-17"]["status"] == "delivered"


def test_a_turn_without_orders_finds_none():
    start_turn({})
    assert "not found" in get_order.func("A-17")["error"]
