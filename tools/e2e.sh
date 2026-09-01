#!/usr/bin/env bash
# Build the macOS player and play the whole declared season in every shipped language.
#
# The acceptance harness proves rules; it never composes a scene, so it cannot show that anything
# is reachable or that anything is on screen. This does the other half: it drives a real build
# through the real UI, screenshots it, and fails on a missing string, a covered control, or any
# error in the log.
#
# WHAT IT COVERS IS NOT LISTED HERE, AND THAT IS DELIBERATE. The runner reads Assets/Resources/Data/
# stages.json and plays whatever the season declares, in order, from the first frame of a cold save
# to the stage that declares itself terminal. This header said "the opening" for a season that had
# grown to three days and "all three days" for one that had grown to nine, and a description that
# goes stale is worse than none: it is what people read instead of the code. The one thing worth
# stating is the tier, because it is a choice rather than a fact — the full battery of checks runs
# on three stages the runner picks out of the table (the first, the one that turns chapter and verse
# on, and the one that ends the season) and every other stage is traversed cheaply.
#
# Usage:
#   tools/e2e.sh                  build, then run every locale, concurrently
#   tools/e2e.sh --no-build       reuse the player already in Builds/mac
#   tools/e2e.sh --locale en      just that one
#   tools/e2e.sh --from-stage 6   AUTHORING ONLY: seed a save at stage 6 and start there
#
# Screenshots and per-locale results land in Builds/e2e/, which is emptied at the start of every
# run: a shorter run's screenshots sitting beside a longer one's read as current evidence, and the
# renumbering that came with the nine-stage season made that collision certain rather than likely.
set -uo pipefail

UNITY_VERSION="${UNITY_VERSION:-6000.3.23f1}"
UNITY_BIN="/Applications/Unity/Hub/Editor/${UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="${ROOT}/Builds/mac/SheepGate.app"
OUT="${ROOT}/Builds/e2e"
STAGES_FILE="${ROOT}/Assets/Resources/Data/stages.json"

# Every locale with a content directory. Discovered rather than listed, so adding a language
# cannot forget to add it here.
LOCALES=()
while IFS= read -r LINE; do LOCALES+=("${LINE}"); done < <(
  find "${ROOT}/Assets/Resources/Data/locales" -mindepth 1 -maxdepth 1 -type d -exec basename {} \; | sort
)

BUILD=1
FROM_STAGE=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-build) BUILD=0; shift ;;
    --locale) LOCALES=("${2:-}"); shift 2 ;;
    --from-stage) FROM_STAGE="${2:-}"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

# Outer ceiling on one player, above the runner's own. The runner already fails a stalled STEP and
# quits; what this catches is the case it cannot, where the runner never installed at all because
# -e2e did not reach HasFlag. That player has nothing in it that ever calls Application.Quit and it
# runs until somebody notices, which on a machine running two locales at once means both.
#
# Sized from the stage count so it tracks the season, and generously: it is a backstop, not a
# measurement, and every real failure should have been reported by the runner long before. The
# count is read out of the stage file by line shape, so a reformatted file only costs the fallback.
STAGE_COUNT="$(grep -c '"day"[[:space:]]*:' "${STAGES_FILE}" 2>/dev/null || true)"
if [[ ! "${STAGE_COUNT}" =~ ^[0-9]+$ || "${STAGE_COUNT}" -lt 1 ]]; then
  STAGE_COUNT=9
fi
PLAYER_TIMEOUT=$(( 180 + 90 * STAGE_COUNT ))

mkdir -p "${ROOT}/Logs"

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

# Emptied rather than merely created. See the header: stale screenshots from a shorter run are the
# most convincing wrong evidence this directory can hold.
rm -rf "${OUT}"
mkdir -p "${OUT}"

if [[ -n "${FROM_STAGE}" ]]; then
  echo "NOTE: --from-stage ${FROM_STAGE} seeds a save and skips everything before that stage."
  echo "      This is an authoring convenience and NOT the gate: the gate is the cold run from a"
  echo "      fresh save, because reachability from the first frame is why this harness exists."
fi

# One player per locale, all at once.
#
# Free concurrency with no correctness cost, which is why it is worth doing: each locale already had
# its own disposable data directory, its own log, its own locale-suffixed screenshots and its own
# result file, so the two runs share nothing but the read-only app bundle. The wall clock stops
# being the sum of the locales and becomes the slowest of them, and a nine-stage season is exactly
# where that stops being a nicety.
run_locale() {
  local locale="$1"
  local data="${OUT}/data-${locale}"
  local log="${ROOT}/Logs/e2e-${locale}.log"
  local console="${OUT}/console-${locale}.txt"

  # A disposable data directory per locale. The runner drives the real SaveSystem, and a run that
  # wrote to the normal location would overwrite somebody's playtest — that has happened here.
  rm -rf "${data}"
  mkdir -p "${data}"

  # One array rather than a conditional fragment spliced into the command line: expanding an empty
  # array under `set -u` is an error on the bash macOS actually ships, and the failure looks like a
  # broken player rather than a broken script.
  local args=(
    -e2e -e2e-out "${OUT}"
    -locale "${locale}"
    -data-path "${data}"
    -logFile "${log}"
    -screen-fullscreen 0 -screen-width 1080 -screen-height 1920
  )
  if [[ -n "${FROM_STAGE}" ]]; then
    args+=(-e2e-start-stage "${FROM_STAGE}")
  fi

  "${BINARY}" "${args[@]}" &
  local player=$!

  # The watcher, and the reason it is a background sleep rather than `timeout`: macOS ships no
  # `timeout`, and coreutils is not something this repo may assume is installed.
  ( sleep "${PLAYER_TIMEOUT}"; kill -0 "${player}" 2>/dev/null && kill -9 "${player}" 2>/dev/null ) &
  local watcher=$!

  wait "${player}"
  local status=$?
  kill "${watcher}" 2>/dev/null
  wait "${watcher}" 2>/dev/null

  {
    echo
    echo "=== ${locale} ==="
    sed -n 's/^\[E2E\] //p' "${log}" 2>/dev/null
    if [[ ${status} -ne 0 ]]; then
      if [[ ${status} -ge 128 ]]; then
        echo "KILLED (${locale}) — no exit after ${PLAYER_TIMEOUT}s. The runner never reported, which"
        echo "usually means -e2e did not reach it at all. Full log: ${log}"
      else
        echo "FAILED (${locale}, exit ${status}) — full log: ${log}"
      fi
    else
      echo "PASSED (${locale})"
    fi
  } > "${console}"

  return ${status}
}

PIDS=()
for LOCALE in "${LOCALES[@]}"; do
  run_locale "${LOCALE}" &
  PIDS+=($!)
done

# Collected in the order the locales were listed, so a concurrent run reads like a sequential one.
STATUS=0
for INDEX in "${!PIDS[@]}"; do
  if ! wait "${PIDS[${INDEX}]}"; then
    STATUS=1
  fi
done

for LOCALE in "${LOCALES[@]}"; do
  cat "${OUT}/console-${LOCALE}.txt" 2>/dev/null
done

echo
echo "Screenshots: ${OUT}"
if [[ ${STATUS} -eq 0 ]]; then
  echo "ALL LOCALES PASSED"
else
  echo "E2E FAILURES — see the logs above"
fi
exit ${STATUS}
