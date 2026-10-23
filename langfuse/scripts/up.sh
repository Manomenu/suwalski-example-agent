#!/usr/bin/env bash
# Starts the local Langfuse (generating its secrets in .env once), waits until the web answers, creates the model definition and
# runs setup.sh: one command leaves everything configured, no step in the UI.
# Usage: langfuse/scripts/up.sh   (from the repository root)
# shellcheck disable=SC2154
set -euo pipefail
# shellcheck source=/dev/null
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

ensure_env
"${compose[@]}" up -d
echo "waiting for Langfuse at $url ..."
for _ in $(seq 1 120); do
  if healthy; then
    echo "Langfuse is up: $url"
    echo "login:    $(env_value LANGFUSE_USER_EMAIL)"
    echo "password: LANGFUSE_USER_PASSWORD in .env"
    ensure_model
    "$(dirname "${BASH_SOURCE[0]}")/setup.sh"
    exit 0
  fi
  sleep 2
done
echo "Langfuse did not answer in time; see: ${compose[*]} logs langfuse-web" >&2
exit 1
