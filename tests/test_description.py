from fastapi.testclient import TestClient

from example_agent.agent import SYSTEM_PROMPT
from example_agent.app import app


def test_contract_is_built_from_the_real_definition(monkeypatch):
    monkeypatch.setenv("OPENAI_MODEL", "some-model")
    monkeypatch.delenv("SUWAGEVAL_LABEL", raising=False)
    response = TestClient(app).get("/suwageval/contract")
    assert response.status_code == 200
    body = response.json()
    assert body["systemPrompt"] == SYSTEM_PROMPT
    assert body["model"] == {"name": "some-model"}
    assert "label" not in body
    tools = {tool["name"]: tool for tool in body["tools"]}
    assert set(tools) == {"get_order", "refund_order"}
    assert tools["refund_order"]["description"].startswith("Refund an order")
    assert tools["refund_order"]["parameters"]["required"] == ["order_id", "reason"]
    assert body["context"]["usesRetrieval"] is False
    [event] = body["context"]["events"]
    assert event["type"] == "state.changed"
    assert event["dataSchema"]["required"] == ["node", "orderId"]


def test_label_comes_from_the_environment(monkeypatch):
    monkeypatch.setenv("OPENAI_MODEL", "some-model")
    monkeypatch.setenv("SUWAGEVAL_LABEL", "abc1234")
    assert TestClient(app).get("/suwageval/contract").json()["label"] == "abc1234"
