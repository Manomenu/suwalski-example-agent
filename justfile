# Entry point for humans: `just` lists every recipe, grouped.

set shell := ["bash", "-euo", "pipefail", "-c"]

[private]
default:
    @just --list --unsorted

# The agent on http://localhost:7010 from source; reads .env (see .env.example)
[group('run')]
run:
    dotnet run --project ExampleAgent

# The agent from the suwexag:local image on :7010; the model settings come from .env
[group('run')]
run-image:
    @[ -f .env ] || { echo "no .env here: cp .env.example .env and fill it in" >&2; exit 1; }
    docker run --rm -p 127.0.0.1:7010:7010 --env-file .env suwexag:local

# Build the suwexag:local image; the SDK comes from the sibling checkout ../suwalski-agent-eval
[group('build')]
build-image:
    @[ -d ../suwalski-agent-eval ] || { echo "../suwalski-agent-eval is missing: the image needs the sibling Suwageval checkout for the SDK" >&2; exit 1; }
    docker build --build-context suwageval=../suwalski-agent-eval -t suwexag:local .

# The tests (xUnit v3, no model calls)
[group('check')]
test:
    dotnet test

# Format the C# with CSharpier (the version pinned in .config/dotnet-tools.json)
[group('check')]
fmt:
    dotnet tool restore
    dotnet csharpier format .
