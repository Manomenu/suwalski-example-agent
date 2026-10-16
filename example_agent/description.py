"""What this agent tells suwageval about itself: GET /suwageval/contract, built from its real definition."""

import os
from typing import Any

from dotenv import load_dotenv

from example_agent.agent import SYSTEM_PROMPT, TOOLS
from example_agent.contract import STATE_CHANGED_SCHEMA


def describe() -> dict[str, Any]:
    """The agent's contract: prompt, model, tools (MAF's own JSON Schemas), context. No model call."""
    load_dotenv()
    description: dict[str, Any] = {
        "systemPrompt": SYSTEM_PROMPT,
        "model": {"name": os.environ["OPENAI_MODEL"]},
        "tools": [
            {"name": tool.name, "description": tool.description, "parameters": tool.parameters()} for tool in TOOLS
        ],
        "context": {
            "usesRetrieval": False,
            "events": [{"type": "state.changed", "dataSchema": STATE_CHANGED_SCHEMA}],
        },
    }
    if label := os.environ.get("SUWAGEVAL_LABEL"):
        description["label"] = label
    return description
