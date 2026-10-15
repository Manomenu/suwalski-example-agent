import json

from agent_framework import Content, Message
from pydantic import TypeAdapter

from example_agent import contract as c


def test_to_messages_replays_history_and_ignores_retrieval_and_events():
    conversation = [
        c.UserMessage(text="Refund A-17"),
        c.ToolCall(id="1", name="get_order", arguments={"order_id": "A-17"}),
        c.ToolCall(id="2", name="get_order", arguments={"order_id": "A-18"}),
        c.ToolResult(call_id="1", content='{"status": "delivered"}'),
        c.ToolResult(call_id="2", content='{"status": "shipped"}'),
        c.Retrieval(query="q", chunks=[]),
        c.Event(type="state.changed", data={}),
        c.AssistantMessage(text="Done"),
    ]
    messages = c.to_messages(conversation)
    assert [m.role for m in messages] == ["user", "assistant", "tool", "tool", "assistant"]
    assert [x.name for x in messages[1].contents] == ["get_order", "get_order"]
    assert messages[1].contents[0].parse_arguments() == {"order_id": "A-17"}
    assert messages[2].contents[0].call_id == "1"
    assert messages[4].text == "Done"


def test_from_messages_maps_calls_results_and_answer_in_order():
    messages = [
        Message("assistant", [Content.from_function_call("1", "get_order", arguments='{"order_id": "A-17"}')]),
        Message("tool", [Content.from_function_result("1", result='{"status": "delivered"}')]),
        Message("assistant", ["Here you go"]),
    ]
    items = c.from_messages(messages)
    assert [i.kind for i in items] == ["ToolCall", "ToolResult", "AssistantMessage"]
    assert items[0].arguments == {"order_id": "A-17"}
    assert items[1].call_id == "1" and not items[1].is_error


def test_error_results_are_flagged():
    messages = [Message("tool", [Content.from_function_result("1", result='{"error": "nope"}')])]
    assert c.from_messages(messages)[0].is_error


def test_refund_adds_state_changed_event_after_its_result():
    items = [
        c.ToolCall(id="1", name="refund_order", arguments={"order_id": "A-17", "reason": "x"}),
        c.ToolResult(call_id="1", content='{"orderId": "A-17", "refunded": 89.9, "reason": "x"}'),
        c.AssistantMessage(text="Refunded"),
    ]
    out = c.add_events(items)
    assert [i.kind for i in out] == ["ToolCall", "ToolResult", "Event", "AssistantMessage"]
    assert out[2].data == {"node": "refunded", "orderId": "A-17"}


def test_no_event_for_a_failed_refund():
    items = [
        c.ToolCall(id="1", name="refund_order", arguments={}),
        c.ToolResult(call_id="1", content='{"error": "shipped"}', is_error=True),
    ]
    assert [i.kind for i in c.add_events(items)] == ["ToolCall", "ToolResult"]


def test_items_use_camel_case_on_the_wire():
    item = TypeAdapter(c.ConversationItem).validate_python({"kind": "ToolResult", "callId": "1", "content": "x"})
    assert item.call_id == "1"
    assert json.loads(item.model_dump_json(by_alias=True)) == {
        "kind": "ToolResult",
        "callId": "1",
        "content": "x",
        "isError": False,
    }
