"""The suwageval contract (v1) and its mapping to Agent Framework messages. The only module that knows both."""

import json
from typing import Annotated, Any, Literal

from agent_framework import Content, Message
from pydantic import BaseModel, ConfigDict, Field
from pydantic.alias_generators import to_camel


class Item(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True, extra="forbid")


class UserMessage(Item):
    kind: Literal["UserMessage"] = "UserMessage"
    text: str


class AssistantMessage(Item):
    kind: Literal["AssistantMessage"] = "AssistantMessage"
    text: str


class ToolCall(Item):
    kind: Literal["ToolCall"] = "ToolCall"
    id: str
    name: str
    arguments: dict[str, Any]
    agent: str | None = None


class ToolResult(Item):
    kind: Literal["ToolResult"] = "ToolResult"
    call_id: str
    content: str
    is_error: bool = False


class Chunk(Item):
    text: str
    source: str
    score: float


class Retrieval(Item):
    kind: Literal["Retrieval"] = "Retrieval"
    query: str
    chunks: list[Chunk]


class Event(Item):
    kind: Literal["Event"] = "Event"
    type: str
    data: dict[str, Any]


ConversationItem = Annotated[
    UserMessage | AssistantMessage | ToolCall | ToolResult | Retrieval | Event,
    Field(discriminator="kind"),
]


class Order(Item):
    status: Literal["processing", "shipped", "delivered", "refunded"]
    items: list[str]
    total: float


class Fixtures(Item):
    """The test data a scenario starts the shop with; its JSON Schema is the contract's context.fixturesSchema."""

    orders: dict[str, Order] = Field(default_factory=dict, description="The shop's orders, by order id, e.g. A-17")


class TurnRequest(Item):
    conversation: list[ConversationItem]
    fixtures: Fixtures | None = None
    # Sent by suwageval only in a simulated scenario: the same for every turn of one simulated conversation.
    conversation_id: str | None = None


def shop_of(fixtures: Fixtures | None) -> dict[str, dict]:
    """The orders a turn starts with: the scenario's, or none."""
    return {} if fixtures is None else {order_id: order.model_dump() for order_id, order in fixtures.orders.items()}


class TurnResponse(Item):
    items: list[ConversationItem]


def to_messages(conversation: list[ConversationItem]) -> list[Message]:
    """Replay a conversation as Agent Framework history. Retrievals and events are not part of it."""
    messages: list[Message] = []
    pending_calls: list[Content] = []

    def flush_calls() -> None:
        if pending_calls:  # parallel tool calls belong to one assistant message
            messages.append(Message("assistant", list(pending_calls)))
            pending_calls.clear()

    for item in conversation:
        if isinstance(item, ToolCall):
            pending_calls.append(Content.from_function_call(item.id, item.name, arguments=item.arguments))
            continue
        flush_calls()
        if isinstance(item, UserMessage):
            messages.append(Message("user", [item.text]))
        elif isinstance(item, AssistantMessage):
            messages.append(Message("assistant", [item.text]))
        elif isinstance(item, ToolResult):
            messages.append(Message("tool", [Content.from_function_result(item.call_id, result=item.content)]))
    flush_calls()
    return messages


def from_messages(messages: list[Message], agent: str) -> list[ConversationItem]:
    """Turn what the agent produced into contract items, in order; every tool call names the LLM agent that made it."""
    items: list[ConversationItem] = []
    for message in messages:
        for content in message.contents:
            if content.type == "text" and message.role == "assistant" and content.text:
                items.append(AssistantMessage(text=content.text))
            elif content.type == "function_call":
                items.append(
                    ToolCall(
                        id=content.call_id, name=content.name, arguments=content.parse_arguments() or {}, agent=agent
                    )
                )
            elif content.type == "function_result":
                text = content.result if isinstance(content.result, str) else json.dumps(content.result)
                items.append(
                    ToolResult(call_id=content.call_id, content=text, is_error=_is_error(text, content.exception))
                )
    return items


def _is_error(text: str, exception: str | None) -> bool:
    """A tool failed when it raised or returned {"error": ...}."""
    if exception:
        return True
    try:
        parsed = json.loads(text)
    except ValueError:
        return False
    return isinstance(parsed, dict) and "error" in parsed


# The shape of `data` of the one event this agent emits; served by GET /suwageval/contract.
STATE_CHANGED_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {"node": {"type": "string", "enum": ["refunded"]}, "orderId": {"type": "string"}},
    "required": ["node", "orderId"],
    "additionalProperties": False,
}


def add_events(items: list[ConversationItem]) -> list[ConversationItem]:
    """Add the one event this agent emits: state.changed after a successful refund_order."""
    names = {item.id: item.name for item in items if isinstance(item, ToolCall)}
    out: list[ConversationItem] = []
    for item in items:
        out.append(item)
        if isinstance(item, ToolResult) and not item.is_error and names.get(item.call_id) == "refund_order":
            order_id = json.loads(item.content)["orderId"]
            out.append(Event(type="state.changed", data={"node": "refunded", "orderId": order_id}))
    return out
