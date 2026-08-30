#!/usr/bin/env bash
# Compile the project headlessly and report C# errors.
# Usage: tools/unity-check.sh [--open]
set -uo pipefail

UNITY_VERSION="${UNITY_VERSION:-6000.3.23f1}"
UNITY_BIN="/Applications/Unity/Hub/Editor/${UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"
PROJECT_PATH="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOG="${PROJECT_PATH}/Logs/compile.log"

if [[ ! -x "${UNITY_BIN}" ]]; then
  echo "Unity ${UNITY_VERSION} not found at ${UNITY_BIN}" >&2
  echo "Install it with:" >&2
  echo "  '/Applications/Unity Hub.app/Contents/MacOS/Unity Hub' -- --headless install \\" >&2
  echo "    --version ${UNITY_VERSION} --changeset 09d2ecc7fb28 --architecture arm64" >&2
  exit 127
fi

mkdir -p "${PROJECT_PATH}/Logs"

if [[ "${1:-}" == "--open" ]]; then
  echo "Opening the project in the Unity editor..."
  exec "${UNITY_BIN}" -projectPath "${PROJECT_PATH}"
fi

echo "Compiling ${PROJECT_PATH} with Unity ${UNITY_VERSION}..."
"${UNITY_BIN}" \
  -batchmode -quit -nographics \
  -projectPath "${PROJECT_PATH}" \
  -logFile "${LOG}" \
  -accept-apiupdate
STATUS=$?

echo
if grep -qiE "No valid Unity Editor license|License is not active|Unable to acquire a license" "${LOG}" 2>/dev/null; then
  echo "BLOCKED: this machine has no active Unity licence."
  echo "Open Unity Hub, sign in, and take the free Personal licence. Then rerun this script."
  exit 2
fi

# Compiler diagnostics look like:  Assets/Scripts/Foo.cs(12,7): error CS0246: ...
ERRORS=$(grep -E "\): error CS[0-9]+" "${LOG}" 2>/dev/null | sort -u)
WARNS=$(grep -cE "\): warning CS[0-9]+" "${LOG}" 2>/dev/null || echo 0)

if [[ -n "${ERRORS}" ]]; then
  echo "COMPILE ERRORS:"
  echo "${ERRORS}"
  echo
  echo "$(echo "${ERRORS}" | wc -l | tr -d ' ') error(s), ${WARNS} warning(s). Full log: ${LOG}"
  exit 1
fi

# A clean compile still fails the project if assets could not be imported.
if grep -qE "^Fatal Error|Unhandled exception" "${LOG}" 2>/dev/null; then
  echo "IMPORT FAILURE — see ${LOG}"
  grep -E "^Fatal Error|Unhandled exception" "${LOG}" | head -5
  exit 1
fi

echo "Compiled clean. ${WARNS} warning(s). Log: ${LOG}"
exit ${STATUS}
