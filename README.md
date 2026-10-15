# example-agent

A tiny customer-support agent for a fake shop, and the first "agent under test" for
[suwageval](../suwalski-agent-eval). Microsoft Agent Framework (`agent-framework`) for the
agent, FastAPI for the HTTP surface, `uv` for the project. Data is in memory; nothing leaves
the process except the model call.

- `get_order(order_id)` — status, items, total. Orders: `A-17` delivered, `A-18` shipped,
  `A-19` processing, `A-20` already refunded.
- `refund_order(order_id, reason)` — refunds only a delivered order, otherwise an error.

## Run

```sh
uv sync
cp .env.example .env        # OPENAI_BASE_URL, OPENAI_API_KEY, OPENAI_MODEL — any OpenAI-compatible endpoint
uv run uvicorn example_agent.app:app --port 7010
```

Gate: `uv run ruff check . && uv run ruff format --check . && uv run pytest` (no model calls).

```sh
curl -s localhost:7010/suwageval/turn -H 'content-type: application/json' -d '{
  "conversation": [{"kind": "UserMessage", "text": "My kettle from order A-17 arrived broken, please refund it."}]
}'
```

## The suwageval contract (v0)

`POST /suwageval/turn` — suwageval sends the conversation so far and gets back what the agent
produced in ONE turn.

- Request: `{ "conversation": [ item, ... ] }`
- Response 200: `{ "items": [ item, ... ] }` — everything the agent produced in this turn, in
  order: its `ToolCall`s, the matching `ToolResult`s, any `Event`s, and the final
  `AssistantMessage`.
- 422 with a message for a malformed request.

Items, discriminated by `kind`:

```json
{"kind":"UserMessage","text":"..."}
{"kind":"AssistantMessage","text":"..."}
{"kind":"ToolCall","id":"...","name":"...","arguments":{}}
{"kind":"ToolResult","callId":"...","content":"...","isError":false}
{"kind":"Retrieval","query":"...","chunks":[{"text":"...","source":"...","score":0.9}]}
{"kind":"Event","type":"...","data":{}}
```

- Incoming `ToolCall`/`ToolResult` items from earlier turns are replayed into the agent's
  history as they were; the agent does not re-run them.
- This agent has no RAG: `Retrieval` items are accepted and ignored (so are `Event`s).
- This agent emits one event type: after a successful refund,
  `{"kind":"Event","type":"state.changed","data":{"node":"refunded","orderId":"A-17"}}`.
- Choices made here: tool arguments are the tools' own parameter names (`order_id`, `reason`);
  `ToolResult.content` is the tool's JSON as text, and `isError` is true when it raised or
  returned `{"error": ...}`. Consecutive `ToolCall`s are replayed as one assistant message.

The mapping between these items and Agent Framework's `Message`/`Content` lives in
`example_agent/contract.py`, tested in `tests/test_contract.py`.

## `suwageval/` — what this agent tells suwageval

The files suwageval reads from this repository, in the format of
[docs/scenario-format.md](https://github.com/Manomenu/suwalski-agent-eval/blob/master/docs/scenario-format.md):

- `agent.yaml` — the agent (`name`, `endpointUrl`) and its version: system prompt, model, tools
  with JSON Schema parameters, the context contract (no `Retrieval`) and the event types it
  emits with a schema for their `data`.
- `scenarios/*.yaml` — one scenario per file (`key`, `tags`, `conversation`) with its `checks`
  inside: `tool-called`, `tool-not-called`, `event`, `text`. (The judge check this agent had in
  its draft comes after the MVP.)

Check them without a server, from this folder, with the `suwageval` CLI (`just cli-install` in
suwalski-agent-eval installs it; or `just cli validate` there, pointing at this folder):

```sh
suwageval validate
```
