#!/usr/bin/env bash
# Run the acceptance harness headlessly. Exits non-zero when a criterion fails.
set -uo pipefail
UNITY_VERSION="${UNITY_VERSION:-6000.3.23f1}"
UNITY="/Applications/Unity/Hub/Editor/${UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mkdir -p "${ROOT}/Logs"
"${UNITY}" -batchmode -quit -nographics -projectPath "${ROOT}" \
  -executeMethod SheepGate.EditorTools.AcceptanceHarness.RunAll \
  -logFile "${ROOT}/Logs/acceptance.log"
STATUS=$?
sed -n "/Sheep Gate acceptance harness/,/CRITERION FAILURE\|ALL CRITERIA PASSED/p" "${ROOT}/Logs/acceptance.log"
exit ${STATUS}
