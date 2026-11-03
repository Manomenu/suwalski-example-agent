#!/usr/bin/env bash
# The full local gate (`just check`): what a change must pass before it is pushed. The code (build with every analyzer, the tests,
# the formatting), then the acceptance criteria of the specs (docs/visual/spec/): `suwageval gate` runs every scenario with an
# acGuardrail against the running agent, with a real model and Suwageval's judge.
#
# CI does not run the acceptance gate, on purpose: it needs Suwageval, the agent running with a model, and the model's key; a CI
# runner has none of them, and a gate that calls a model on every push costs money and depends on a service outside the repository.
# CI runs the code part only (`just check-code`). The acceptance gate is part of the local full gate, the way this repository is
# meant to be copied.
set -euo pipefail
cd "$(dirname "$0")/.."

"$(dirname "$0")/check-code.sh"

echo "==> acceptance criteria (suwageval gate)"
./scripts/require-suwageval.sh
endpoint=$(sed -n 's/^endpointUrl:[[:space:]]*//p' suwageval/agent.yaml)
if ! curl -sf --max-time 5 "${endpoint%/}/suwageval/contract" >/dev/null; then
  echo "The agent does not answer at $endpoint: start it (just run) and run the gate again." >&2
  exit 1
fi
suwageval validate
suwageval push >/dev/null
suwageval gate
