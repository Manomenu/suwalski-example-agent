"""The support agent: Microsoft Agent Framework over any OpenAI-compatible endpoint."""

import os
from functools import lru_cache

from agent_framework import Agent, Message
from agent_framework.openai import OpenAIChatCompletionClient
from dotenv import load_dotenv

from example_agent.tools import get_order, refund_order

SYSTEM_PROMPT = """You are a polite customer support agent for Kettle & Co., an online shop.
Answer in the language the customer writes in.
Use get_order to look up an order before you talk about it or refund it.
Never refund an order without an order id: if the customer has not given one, ask for it.
Always look the order up with get_order before calling refund_order; refund only what the lookup shows is delivered.
Keep answers short."""


@lru_cache
def build_agent() -> Agent:
    load_dotenv()
    client = OpenAIChatCompletionClient(
        model=os.environ["OPENAI_MODEL"],
        api_key=os.environ["OPENAI_API_KEY"],
        base_url=os.environ["OPENAI_BASE_URL"],
    )
    return Agent(client, SYSTEM_PROMPT, name="support", tools=[get_order, refund_order])


async def run_turn(history: list[Message]) -> list[Message]:
    """Run the agent over the history; return the messages it produced (tool calls, results, answer)."""
    response = await build_agent().run(history)
    return list(response.messages)
