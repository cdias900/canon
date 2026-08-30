#!/usr/bin/env bash
# Build the macOS player and play the opening in every shipped language.
#
# The acceptance harness proves rules; it never composes a scene, so it cannot show that anything
# is reachable or that anything is on screen. This does the other half: it drives a real build
# through the real UI, screenshots it, and fails on a missing string, a covered control, or any
# error in the log.
#
# Usage:
#   tools/e2e.sh                  build, then run every locale
#   tools/e2e.sh --no-build       reuse the player already in Builds/mac
#   tools/e2e.sh --locale en      just that one
#
# Screenshots and per-locale results land in Builds/e2e/.
set -uo pipefail

UNITY_VERSION="${UNITY_VERSION:-6000.3.23f1}"
UNITY_BIN="/Applications/Unity/Hub/Editor/${UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="${ROOT}/Builds/mac/SheepGate.app"
OUT="${ROOT}/Builds/e2e"

# Every locale with a content directory. Discovered rather than listed, so adding a language
# cannot forget to add it here.
LOCALES=()
while IFS= read -r LINE; do LOCALES+=("${LINE}"); done < <(
  find "${ROOT}/Assets/Resources/Data/locales" -mindepth 1 -maxdepth 1 -type d -exec basename {} \; | sort
)

BUILD=1
while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-build) BUILD=0; shift ;;
    --locale) LOCALES=("${2:-}"); shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

mkdir -p "${ROOT}/Logs" "${OUT}"

if [[ ! -x "${UNITY_BIN}" ]]; then
  echo "Unity ${UNITY_VERSION} not found at ${UNITY_BIN}" >&2
  exit 127
fi

if [[ ${BUILD} -eq 1 ]]; then
  echo "Building the macOS player..."
  "${UNITY_BIN}" -batchmode -quit -nographics -projectPath "${ROOT}" \
    -executeMethod SheepGate.EditorTools.BuildScript.BuildMac \
    -logFile "${ROOT}/Logs/e2e-build.log"
  if [[ $? -ne 0 ]]; then
    echo "BUILD FAILED — see Logs/e2e-build.log" >&2
    grep -E "\): error CS|Build failed|BuildFailedException" "${ROOT}/Logs/e2e-build.log" | head -20
    exit 1
  fi
  echo "Built ${APP}"
fi

# Derived, never hardcoded: the executable inside the bundle is named from PlayerSettings
# productName, so spelling it here means a product rename silently breaks the run with a
# "no such file" that reads like a failed build. Resolved after the build, not before it.
BINARY="$(find "${APP}/Contents/MacOS" -maxdepth 1 -type f -perm -u+x 2>/dev/null | head -1)"

# The binary is launched directly rather than through `open`, because `open` returns as soon as
# the app is handed to LaunchServices: it reports neither the exit code nor the player's log.
if [[ -z "${BINARY}" || ! -x "${BINARY}" ]]; then
  echo "No player binary inside ${APP}. Run without --no-build." >&2
  exit 1
fi

STATUS=0
for LOCALE in "${LOCALES[@]}"; do
  echo
  echo "=== ${LOCALE} ==="

  # A disposable data directory per locale. The runner drives the real SaveSystem, and a run that
  # wrote to the normal location would overwrite somebody's playtest — that has happened here.
  DATA="${OUT}/data-${LOCALE}"
  rm -rf "${DATA}"
  mkdir -p "${DATA}"

  LOG="${ROOT}/Logs/e2e-${LOCALE}.log"
  "${BINARY}" \
    -e2e -e2e-out "${OUT}" \
    -locale "${LOCALE}" \
    -data-path "${DATA}" \
    -logFile "${LOG}" \
    -screen-fullscreen 0 -screen-width 1080 -screen-height 1920
  RUN_STATUS=$?

  sed -n 's/^\[E2E\] //p' "${LOG}" 2>/dev/null

  if [[ ${RUN_STATUS} -ne 0 ]]; then
    echo "FAILED (${LOCALE}, exit ${RUN_STATUS}) — full log: ${LOG}"
    STATUS=1
  else
    echo "PASSED (${LOCALE})"
  fi
done

echo
echo "Screenshots: ${OUT}"
if [[ ${STATUS} -eq 0 ]]; then
  echo "ALL LOCALES PASSED"
else
  echo "E2E FAILURES — see the logs above"
fi
exit ${STATUS}
