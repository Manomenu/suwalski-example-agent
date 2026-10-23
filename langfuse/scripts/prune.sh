#!/usr/bin/env bash
# Stops the local Langfuse and removes its volumes: every trace and the project are lost.
# Usage: langfuse/scripts/prune.sh [--yes]   (from the repository root)
# shellcheck disable=SC2154
set -euo pipefail
# shellcheck source=/dev/null
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

if [ "${1:-}" != "--yes" ]; then
  read -r -p "Remove Langfuse's containers and volumes (all its data is lost)? [y/N] " answer
  case "$answer" in
    y | Y | yes) ;;
    *) echo "cancelled"; exit 1 ;;
  esac
fi
ensure_env
"${compose[@]}" down --volumes
echo "Langfuse stopped and its volumes removed"
