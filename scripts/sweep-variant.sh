#!/usr/bin/env bash
# Switches the sweep instance of the agent (port 7011, never the developer's 7010) to a variant, or stops it.
# Usage: scripts/sweep-variant.sh <variant>|stop   (run from the repository root; `suwageval sweep` does that)
set -euo pipefail

port=7011
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifacts="$root/.artifacts"
pidfile="$artifacts/sweep.pid"

stop() {
  if [ -f "$pidfile" ]; then
    local pgid
    pgid="$(cat "$pidfile")"
    rm -f "$pidfile"
    if [ -n "$pgid" ]; then
      kill -TERM -- "-$pgid" 2>/dev/null || true
      for _ in $(seq 1 50); do
        kill -0 -- "-$pgid" 2>/dev/null || return 0
        sleep 0.1
      done
      kill -KILL -- "-$pgid" 2>/dev/null || true
    fi
  fi
}

[ $# -eq 1 ] || { echo "usage: $0 <variant>|stop" >&2; exit 2; }

if [ "$1" = stop ]; then
  stop
  exit 0
fi

variant="$1"
file="$root/suwageval/variants/$variant.env"
[ -f "$file" ] || { echo "no such variant: $file" >&2; exit 1; }

stop
mkdir -p "$artifacts"
# The variant's variables, the label and the port; the model settings come from .env as usual.
set -a
# shellcheck disable=SC1090
. "$file"
set +a
export SUWAGEVAL_LABEL="$variant" Urls="http://localhost:$port"
# The built dll (a quick switch), started from the root so it finds .env; `dotnet run` would start it in ExampleAgent/.
export ASPNETCORE_CONTENTROOT="$root/ExampleAgent"
dll="$root/ExampleAgent/bin/Debug/net10.0/ExampleAgent.dll"
[ -f "$dll" ] || { echo "not built: run dotnet build first ($dll)" >&2; exit 1; }
cd "$root"
setsid dotnet "$dll" >"$artifacts/sweep-$variant.log" 2>&1 </dev/null &
echo "$!" >"$pidfile" # setsid makes it a group leader: its PID is the group's PGID
