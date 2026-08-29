#!/usr/bin/env bash
# Points git at the hooks tracked in tools/hooks.
#
# Git never shares .git/hooks, so a hook only takes effect on a clone once somebody opts in.
# One command, once per clone:
#
#   tools/install-hooks.sh
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT}" || exit 1

# core.hooksPath replaces .git/hooks wholesale rather than adding to it. Nothing in this
# repository ships hooks there - it holds only git's own .sample files - but say so, because
# anyone with a personal hook installed would otherwise wonder where it went.
EXISTING="$(find .git/hooks -type f ! -name '*.sample' 2>/dev/null | head -5)"
if [[ -n "${EXISTING}" ]]; then
  echo "Note: .git/hooks holds hooks that core.hooksPath will bypass:"
  echo "${EXISTING}" | sed 's/^/  /'
  echo
fi

git config core.hooksPath tools/hooks
echo "core.hooksPath -> tools/hooks"
echo
echo "Active hooks:"
for hook in tools/hooks/*; do
  [[ -f "${hook}" ]] || continue
  if [[ -x "${hook}" ]]; then
    echo "  $(basename "${hook}")"
  else
    echo "  $(basename "${hook}")  NOT EXECUTABLE - run: chmod +x ${hook}"
  fi
done
echo
echo "Commit messages are now checked for English. Verify the history with:"
echo "  node tools/check-commit-message.mjs --range origin/main..HEAD"
