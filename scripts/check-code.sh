#!/usr/bin/env bash
# The code part of the gate (`just check-code`): no model, no Suwageval, nothing running. What CI runs; `just check` runs it first.
set -euo pipefail
cd "$(dirname "$0")/.."

echo "==> build (warnings are errors: nullable, every analyzer, code style)"
dotnet build --nologo -v q
echo "==> tests (xUnit v3, no model calls)"
dotnet test --no-build
echo "==> formatting (csharpier)"
dotnet tool restore >/dev/null
dotnet csharpier check .
