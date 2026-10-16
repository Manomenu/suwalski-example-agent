"""HTTP surface: the suwageval contract (v0) and nothing else."""

from collections.abc import Awaitable, Callable
from typing import Annotated, Any

from agent_framework import Message
from fastapi import Depends, FastAPI

from example_agent import contract, tools
from example_agent.agent import run_turn
from example_agent.description import describe

Runner = Callable[[list[Message]], Awaitable[list[Message]]]

app = FastAPI(title="example-agent")


@app.get("/suwageval/contract")
def contract_description() -> dict[str, Any]:
    return describe()


def get_runner() -> Runner:
    return run_turn


@app.post("/suwageval/turn", response_model_by_alias=True)
async def turn(request: contract.TurnRequest, runner: Annotated[Runner, Depends(get_runner)]) -> contract.TurnResponse:
    tools.start_turn()
    produced = await runner(contract.to_messages(request.conversation))
    return contract.TurnResponse(items=contract.add_events(contract.from_messages(produced)))
