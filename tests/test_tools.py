import pytest

from example_agent.tools import ORDERS, get_order, refund_order, reset_orders


@pytest.fixture(autouse=True)
def fresh_shop():
    reset_orders()


def test_get_order_found():
    result = get_order.func("A-17")
    assert result["status"] == "delivered"
    assert result["total"] == 89.90


def test_get_order_unknown():
    assert "not found" in get_order.func("Z-1")["error"]


def test_refund_delivered_order():
    result = refund_order.func("A-17", "broken kettle")
    assert result["orderId"] == "A-17"
    assert ORDERS["A-17"]["status"] == "refunded"


def test_refund_shipped_order_is_refused():
    result = refund_order.func("A-18", "changed my mind")
    assert "only a delivered order" in result["error"]
    assert ORDERS["A-18"]["status"] == "shipped"


def test_refund_twice_is_refused():
    refund_order.func("A-17", "x")
    assert "error" in refund_order.func("A-17", "x")
