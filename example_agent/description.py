"""What this agent tells suwageval about itself: GET /suwageval/contract, built from its real definition."""

import os
from typing import Any

from dotenv import load_dotenv

from example_agent.agent import AGENT_NAME, SYSTEM_PROMPT, TOOLS
from example_agent.contract import STATE_CHANGED_SCHEMA, Fixtures


def describe() -> dict[str, Any]:
    """The agent's contract (v1): its one LLM agent (prompt, model, MAF's tool schemas) and context. No model call."""
    load_dotenv()
    description: dict[str, Any] = {
        "agents": [
            {
                "name": AGENT_NAME,
                "model": {"name": os.environ["OPENAI_MODEL"]},
                "config": SYSTEM_PROMPT,
                "tools": [
                    {"name": tool.name, "description": tool.description, "parameters": tool.parameters()}
                    for tool in TOOLS
                ],
            }
        ],
        "context": {
            "usesRetrieval": False,
            "events": [{"type": "state.changed", "dataSchema": STATE_CHANGED_SCHEMA}],
            "fixturesSchema": Fixtures.model_json_schema(by_alias=True),
        },
    }
    if label := os.environ.get("SUWAGEVAL_LABEL"):
        description["label"] = label
    return description
