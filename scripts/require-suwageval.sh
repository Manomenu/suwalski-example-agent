#!/usr/bin/env bash
# Fails with what to do when the suwageval CLI cannot be run: not installed, or installed where PATH does not look.
set -euo pipefail

command -v suwageval >/dev/null && exit 0

tool="$HOME/.dotnet/tools/suwageval"
if [ -x "$tool" ]; then
  cat >&2 <<MSG
suwageval is installed ($tool) but ~/.dotnet/tools is not on PATH. Add it to your shell's rc file (~/.zshrc, ~/.bashrc):

    export PATH="\$HOME/.dotnet/tools:\$PATH"

then open a new terminal (or source the file) and run the recipe again.
MSG
else
  cat >&2 <<MSG
The suwageval CLI is not installed. Install it from the Suwageval checkout next to this one (needs .NET SDK 10 and just):

    cd ../suwalski-agent-eval && just cli-install

It lands in ~/.dotnet/tools, which must be on PATH: export PATH="\$HOME/.dotnet/tools:\$PATH" in your shell's rc file.
Run just cli-install again after pulling suwalski-agent-eval, so the CLI matches the server.
MSG
fi
exit 1
