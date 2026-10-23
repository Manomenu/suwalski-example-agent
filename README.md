# suwexag — the Suwageval example agent

**suwexag** is a tiny customer-support agent for a fake shop (Kettle & Co.), and the example "agent under test" for
[Suwageval](../suwalski-agent-eval). ASP.NET Core on .NET 10, Microsoft Agent Framework (`Microsoft.Agents.AI`) over any
OpenAI-compatible endpoint, and the C# SDK `Suwageval.AgentSdk` for the two routes Suwageval talks to. Data is in memory;
nothing leaves the process except the model call.

The one LLM agent is named `support` and has three tools:

- `get_order(order_id)`: status, items and total. The shop has no orders of its own: they come from the scenario's `fixtures`.
- `refund_order(order_id, reason)`: refunds only a delivered order, otherwise an error. A successful refund emits the event
  `state.changed` (`{"node":"refunded","orderId":"A-17"}`).
- `search_policy(query)`: a keyword lookup over three policy paragraphs in code (`ExampleAgent/Policy/PolicyIndex.cs`:
  refunds, shipping, warranty). It makes the agent a small RAG: the turn carries a `Retrieval` item and the answer lists the
  retrieved paragraphs as its `sources`.

## Requirements

- .NET 10 SDK.
- **The sibling checkout `../suwalski-agent-eval`**: the SDK is not published to a feed yet, so `ExampleAgent.csproj` has a
  `ProjectReference` to `../../suwalski-agent-eval/Suwageval.AgentSdk/Suwageval.AgentSdk.csproj`. It becomes a
  `PackageReference` once the package is published. `ExampleAgent.slnx` also lists the SDK project (solution folder `/sdk/`) so the editor (C# Dev Kit) gives full
  editing across both repositories; the sibling checkout is part of the solution until the SDK is a `PackageReference`.
- A model: any OpenAI-compatible endpoint.

## Run

From source:

```sh
cp .env.example .env        # OPENAI_BASE_URL, OPENAI_API_KEY, OPENAI_MODEL
just run                    # dotnet run --project ExampleAgent: http://localhost:7010 (appsettings.json)
curl -s localhost:7010/suwageval/contract
```

From the image `suwexag:local`, for using suwexag without its source or from another stack (the `Dockerfile` takes the SDK
from the sibling checkout through a named build context):

```sh
just build-image            # docker build --build-context suwageval=../suwalski-agent-eval -t suwexag:local .
just run-image              # docker run --rm -p 127.0.0.1:7010:7010 --env-file .env suwexag:local
```

In the container the agent listens on `0.0.0.0:7010` and needs no `.env` file inside: the settings come from `--env-file`.
Suwageval's `just tmux-dev` runs suwexag from source first (`dotnet watch --project ExampleAgent` in this repository, reading its
`.env`) and falls back to the `suwexag:local` image only when the source project is not there; it refuses to start when neither
exists. `just` lists all recipes.

`.env` is read at start-up into the environment (a variable that is already set wins; `ExampleAgent/DotEnv.cs`). An optional
`SUWAGEVAL_LABEL` (e.g. the git SHA) names the running version in Suwageval.

A turn by hand:

```sh
curl -s localhost:7010/suwageval/turn -H 'content-type: application/json' -d '{
  "conversation": [{"kind": "UserMessage", "text": "My kettle from order A-17 arrived broken, please refund it."}],
  "fixtures": {"orders": {"A-17": {"status": "delivered", "items": ["Kettle"], "total": 89.9}}}
}'
```

## Sweep

`suwageval sweep` runs the scenarios once per variant of the agent and puts the runs in one run group, to compare them on the web.
Two variants live in `suwageval/variants/` (the name is the file name): `baseline` (`SUWEXAG_PROMPT=default`, the prompt as it is)
and `concise` (`SUWEXAG_PROMPT=concise`: the same rules, answers of one or two sentences). The prompt is the agent's `config` in the
contract, so each is a version of its own in Suwageval. `SUWEXAG_PROMPT` is optional (unset means `default`; any other value stops
the agent at start).

```sh
just sweep                                    # every scenario, both variants
just sweep --tag refunds --tag simulation     # scenarios with any of the tags
just sweep baseline concise                   # variants by name
just sweep --select                           # choose the variants
```

It needs the `suwageval` CLI on `PATH` (`just cli-install` in `../suwalski-agent-eval`, `~/.dotnet/tools` on `PATH`); without it the recipe stops and says which of the two is missing. Every option passes through to `suwageval sweep`. The recipe builds once, runs `suwageval push` (the sweep does not push by itself,
and its runs need the scenarios on the server), then sweeps. `scripts/sweep-variant.sh <variant>|stop` (the `sweep:` section of
`suwageval/agent.yaml`) starts the built agent with the variant's variables and `SUWAGEVAL_LABEL=<variant>` on **port 7011**, so the
agent you develop against on 7010 is left alone; `stop` ends it (the sweep calls it at the end). The log of each variant is
`.artifacts/sweep-<variant>.log`. The sweep prints a row per variant and a link (the run group) to compare them on the web.

## Gate

```sh
dotnet build        # warnings are errors: nullable, every analyzer (latest-all), code style
dotnet test         # xUnit v3; no model calls (a scripted chat client stands in for the model)
dotnet tool restore && dotnet csharpier check .    # formatting (dotnet csharpier format . fixes it)
```

## What it shows of the SDK

| SDK feature | Where |
| --- | --- |
| `IAgentContractSource`, `AgentContract` | `Support/SupportContractSource.cs`: the contract is built from the real definition (prompt, model name, the tools' JSON Schemas as the model gets them) |
| `ITurnHandler`, `MapSuwagevalAgent()` | `Support/SupportTurnHandler.cs`, `Program.cs` |
| `request.FixturesAs<T>()` | the turn reads the scenario's orders as `ShopFixtures`; a shape it does not declare is a 400 |
| `ContractSchema.ObjectSchema` / `EnumSchema<T>()` | `Shop/ShopFixtures.cs` (`context.fixturesSchema`) and the schema of the `state.changed` event's data |
| `request.HistoryTurns()` | `Support/ChatMapping.cs` replays earlier turns, every tool call paired with its result, as chat history |
| `TurnRejectedException` | a history that cannot be one (thrown by the SDK), tool arguments that are not an object; the turn is answered 400 with the reason |
| `request.Correlation` | the `Suwageval-Run-Id`, `-Scenario` and `-Sample` headers go to the log, never into what the agent does |
| `request.ConversationId` | `Shop/ShopStore.cs`: see below |
| `AgentContractRules.Problems` | `ExampleAgent.Tests/ContractTests.cs` asserts the served contract has none |
| `Retrieval` items, `usesRetrieval` + `dataVersion` | `Policy/PolicyIndex.cs` (the data version is a hash of the paragraphs), `Support/ChatMapping.cs` |

**State per conversation.** Suwageval runs scenarios in parallel, so a conversation must not see another one's refunds. Without
a `conversationId` every turn starts from a fresh copy of the fixtures. A simulated conversation sends the same id with every
turn: its first turn starts fresh, later turns go on from what earlier ones left (an order refunded in turn 2 is refunded in
turn 3). The shop is kept in memory and never evicted: fine for an example, not for production.

## `suwageval/`: agent.yaml and scenarios

Format: [docs/scenario-format.md](https://github.com/Manomenu/suwalski-agent-eval/blob/master/docs/scenario-format.md). The
agent's version is not written down: Suwageval reads it from `GET /suwageval/contract`.

| Scenario | Shows |
| --- | --- |
| `greeting-pl` | `judge` checks |
| `order-status` | `fixtures`, `tool-called` with `args` and `result`, `by: support`, `text` (`pattern`), `tool-not-called`, a check's own `samples` |
| `order-not-found` | `$pattern` in `args` and `result`, `text` `notContains` |
| `refund-happy-path` | `$judge` matcher for a free-text argument, `where` with `turn`, `after` and `before`, `event` with `data`, `passThreshold` |
| `refund-follow-up` | a conversation that already holds tool calls and results (replayed as history) |
| `refund-not-delivered` | what must never happen: `tool-not-called`, `event-not-emitted` |
| `refund-already-refunded` | `event-not-emitted` with `data` |
| `refund-no-order-id` | the agent must ask instead of acting |
| `shipping-policy` | retrieval: `cited`, `$judge` on the search query |
| `simulation-angry-refund-not-delivered` | simulation: an angry customer demands a refund of a shipped order; it must never be refunded |
| `simulation-refund-over-several-turns` | simulation: a legitimate refund reached over several turns; refund called after the lookup, the state event, and no refund after `state.changed` (state kept per `conversationId`) |

Every scenario has `tags` (`regression`, `smoke`, `refunds`, `simulation`, ...). The simulations need the server to have a
simulator model configured (`Simulation:Model`), see the Simulation section of the format document.

```sh
suwageval validate            # names checked when the agent is up on :7010
suwageval validate --offline  # structure only
suwageval push                # the agent, its scenarios and checks
suwageval run --tag regression --wait
suwageval run --tag simulation --wait
```

`push` and `run` talk to Suwageval through its web app (`SUWAGEVAL_URL`, default `http://localhost:3230`).

## Layout

```
ExampleAgent/            the agent (Program.cs, DotEnv.cs, ModelSettings.cs)
  Support/               the agent, its contract, the turn handler, the mapping to chat messages
  Shop/                  orders, fixtures, tools, the per-conversation store
  Policy/                the policy lookup
ExampleAgent.Tests/      xUnit v3 tests
suwageval/               agent.yaml and the scenarios
```

## Langfuse

A local, self-hosted Langfuse (v4: web, worker, PostgreSQL, ClickHouse, Redis, MinIO; all images pinned in
`langfuse/compose.yaml`) collects the agent's traces. It needs Docker (or podman behind `docker`) with Compose v2.

```sh
just langfuse up        # generates the secrets in .env once, starts the stack, waits for it
just langfuse status    # the address, whether the web answers, each service's state
just langfuse down      # stops it, keeps the data
just langfuse prune     # stops it and removes the volumes (asks first; --yes skips; all traces are lost)
just langfuse setup     # sets Claude Code up for it (below)
```

- Address: http://localhost:3290 (only the web is published, on 127.0.0.1). Login: the e-mail `LANGFUSE_USER_EMAIL` from
  `.env` (default `suwexag@example.com`), password `LANGFUSE_USER_PASSWORD` in `.env`. The organization and project
  `suwexag` and the project's API keys (`LANGFUSE_PUBLIC_KEY`, `LANGFUSE_SECRET_KEY`) exist after the first start
  (Langfuse's `LANGFUSE_INIT_*` variables); the secrets are generated into the gitignored `.env`, names in `.env.example`.
- The OpenTelemetry exporter settings are committed in `ExampleAgent/appsettings.json`
  (`OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:3290/api/public/otel`, `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`;
  Langfuse takes OTLP over HTTP, not gRPC). The auth header is built from the keys in `.env`, never committed.
- `just langfuse setup` for Claude Code: copies the official Langfuse skill (github.com/langfuse/skills, `skills/langfuse`, pinned
  to a commit in `langfuse/scripts/setup.sh`) into `.claude/skills/langfuse/` (committed), and writes `LANGFUSE_PUBLIC_KEY`,
  `LANGFUSE_SECRET_KEY` and `LANGFUSE_BASE_URL` into `.claude/settings.local.json` (gitignored). Restart Claude Code afterwards.
