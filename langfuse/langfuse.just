# The local self-hosted Langfuse (traces of the agent). Recipes run from the repository root.

set working-directory := ".."

# Start Langfuse (generates its secrets in .env once) and wait until it answers on http://localhost:3290
up:
    @langfuse/scripts/up.sh

# Stop Langfuse, keep its data
down:
    @langfuse/scripts/down.sh

# Stop Langfuse and remove its volumes (asks first; pass --yes to skip): all traces are lost
prune *args:
    @langfuse/scripts/prune.sh {{args}}

# The address, whether the web answers and each service's state
status:
    @langfuse/scripts/status.sh

# Set Claude Code up for Langfuse: the official skill into .claude/skills/langfuse and the keys into .claude/settings.local.json
setup:
    @langfuse/scripts/setup.sh
