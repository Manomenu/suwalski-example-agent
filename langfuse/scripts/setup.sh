#!/usr/bin/env bash
# Sets up what this machine needs and the repository must not hold: the model definition for the agent's model in Langfuse
# (its name comes from OPENAI_MODEL in the gitignored .env, never from a committed file), and Claude Code for the local
# Langfuse: the official Langfuse skill (pinned copy in .claude/skills/langfuse/) and the credentials in
# .claude/settings.local.json (gitignored), taken from .env.
# Idempotent. Usage: langfuse/scripts/setup.sh   (from the repository root)
# shellcheck disable=SC2154
set -euo pipefail
# shellcheck source=/dev/null
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

# https://github.com/langfuse/skills pinned to this commit (the newest one that was at least two weeks old); change it deliberately.
skills_commit=b86532b77f16f1e27c7cffa32bb632aa3cd9a352

ensure_env

if healthy; then
  ensure_model
else
  echo "model definition: skipped (Langfuse is not running; just langfuse up runs setup again)"
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
git -C "$work" init -q
git -C "$work" fetch -q --depth 1 https://github.com/langfuse/skills.git "$skills_commit"
git -C "$work" checkout -q FETCH_HEAD
rm -rf .claude/skills/langfuse
mkdir -p .claude/skills
cp -r "$work/skills/langfuse" .claude/skills/langfuse
echo "skill: .claude/skills/langfuse (github.com/langfuse/skills @ $skills_commit)"

settings=.claude/settings.local.json
[ -f "$settings" ] || echo '{}' >"$settings"
tmp="$(mktemp)"
jq --arg pk "$(env_value LANGFUSE_PUBLIC_KEY)" --arg sk "$(env_value LANGFUSE_SECRET_KEY)" --arg url "$url" \
  '.env = ((.env // {}) + {LANGFUSE_PUBLIC_KEY: $pk, LANGFUSE_SECRET_KEY: $sk, LANGFUSE_BASE_URL: $url})' "$settings" >"$tmp"
mv "$tmp" "$settings"
echo "credentials: LANGFUSE_PUBLIC_KEY, LANGFUSE_SECRET_KEY, LANGFUSE_BASE_URL=$url in $settings (gitignored)"
echo "restart Claude Code in this folder so it picks the skill and the environment up"
