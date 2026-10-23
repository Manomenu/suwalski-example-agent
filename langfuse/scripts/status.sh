#!/usr/bin/env bash
# Prints the address, whether the web answers and each service's state.
# Usage: langfuse/scripts/status.sh   (from the repository root)
# shellcheck disable=SC2154
set -euo pipefail
# shellcheck source=/dev/null
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

echo "address: $url"
if healthy; then
  echo "web:     answers ($url/api/public/health)"
else
  echo "web:     DOWN"
fi
if [ -f .env ] && grep -q '^LANGFUSE_POSTGRES_PASSWORD=' .env; then
  "${compose[@]}" ps --all --format '{{.Service}}: {{.State}} {{.Health}}'
else
  echo "services: not started yet (just langfuse up)"
fi
