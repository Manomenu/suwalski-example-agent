#!/usr/bin/env bash
# Stops the local Langfuse and keeps its data (the volumes).
# Usage: langfuse/scripts/down.sh   (from the repository root)
# shellcheck disable=SC2154
set -euo pipefail
# shellcheck source=/dev/null
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

ensure_env
"${compose[@]}" down
echo "Langfuse stopped; its data is kept (just langfuse prune removes it)"
