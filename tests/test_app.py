from agent_framework import Content, Message
from fastapi.testclient import TestClient

from example_agent.app import app, get_runner

TURN = [
    Message(
        "assistant", [Content.from_function_call("1", "refund_order", arguments={"order_id": "A-17", "reason": "x"})]
    ),
    Message("tool", [Content.from_function_result("1", result='{"orderId": "A-17", "refunded": 89.9, "reason": "x"}')]),
    Message("assistant", ["Refunded."]),
]


async def fake_runner(history):
    assert history[0].text == "Refund A-17"
    return TURN


def client() -> TestClient:
    app.dependency_overrides[get_runner] = lambda: fake_runner
    return TestClient(app)


def test_turn_returns_items_with_event():
    response = client().post("/agenteval/turn", json={"conversation": [{"kind": "UserMessage", "text": "Refund A-17"}]})
    assert response.status_code == 200
    items = response.json()["items"]
    assert [i["kind"] for i in items] == ["ToolCall", "ToolResult", "Event", "AssistantMessage"]
    assert items[1]["callId"] == "1" and items[1]["isError"] is False
    assert items[2] == {"kind": "Event", "type": "state.changed", "data": {"node": "refunded", "orderId": "A-17"}}


def test_malformed_request_is_422():
    for body in ({}, {"conversation": [{"kind": "Nope"}]}, {"conversation": [{"kind": "UserMessage"}]}):
        assert client().post("/agenteval/turn", json=body).status_code == 422
