#!/usr/bin/env bash
# Prints what a person needs to use the local Langfuse: where it is, how to log in, the project and its API keys, where the agent
# sends its traces and whether Claude Code is set up. Everything comes from .env (gitignored) and the committed settings.
# Usage: langfuse/scripts/info.sh   (from the repository root)
# shellcheck disable=SC2154
set -euo pipefail
# shellcheck source=/dev/null
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

if ! [ -f .env ] || ! grep -q '^LANGFUSE_PUBLIC_KEY=' .env; then
  echo "Langfuse is not set up yet: just langfuse up"
  exit 0
fi

state="DOWN (just langfuse up)"
healthy && state="up"
echo "address:     $url  ($state)"
echo "login:       $(env_value LANGFUSE_USER_EMAIL)"
echo "password:    $(env_value LANGFUSE_USER_PASSWORD)"
echo "project:     suwexag"
echo "public key:  $(env_value LANGFUSE_PUBLIC_KEY)"
echo "secret key:  $(env_value LANGFUSE_SECRET_KEY)"
echo "traces to:   $url/api/public/otel  (OTEL_EXPORTER_OTLP_ENDPOINT, ExampleAgent/appsettings.json)"
if [ -d .claude/skills/langfuse ] && [ -f .claude/settings.local.json ]; then
  echo "Claude Code: skill and keys set up (.claude/skills/langfuse, .claude/settings.local.json)"
else
  echo "Claude Code: not set up (just langfuse setup)"
fi
echo "a run's traces: filter metadata suwageval.run.id = <run id>; one sample = one session (its conversation id)"
