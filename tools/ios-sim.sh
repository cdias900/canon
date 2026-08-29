#!/usr/bin/env bash
# Build, install and run the iOS player on a simulator.
#
# A Unity iOS build is an Xcode project, not an app, so getting to something runnable takes
# three steps: export from Unity, compile with Xcode, install on a booted simulator. This
# wraps all three because the two settings that make it work are not discoverable from any
# error message — see BuildScript.BuildIOSSimulator for the architecture trap.
#
# Usage:
#   tools/ios-sim.sh                 export, compile, install, launch
#   tools/ios-sim.sh build           export and compile only
#   tools/ios-sim.sh run             install and launch on the booted simulator
#   tools/ios-sim.sh shot [name]     write a screenshot to Logs/
#   tools/ios-sim.sh reset           uninstall, which clears the save and the telemetry
#
#   --device "iPhone 17 Pro"         which simulator to boot (default below)
set -uo pipefail

UNITY_VERSION="6000.3.23f1"
UNITY_BIN="/Applications/Unity/Hub/Editor/${UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXPORT_DIR="${ROOT}/Builds/ios-sim"
DERIVED="${EXPORT_DIR}/DerivedData"
PRODUCT="${DERIVED}/Build/Products/Debug-iphonesimulator/PortadasOvelhas.app"
BUNDLE_ID="com.createhack.portadasovelhas"
DEVICE="iPhone 17 Pro"

COMMAND="all"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --device) DEVICE="${2:-}"; shift 2 ;;
    build|run|shot|reset|all) COMMAND="$1"; shift ;;
    *) SHOT_NAME="$1"; shift ;;
  esac
done

mkdir -p "${ROOT}/Logs"

# The device has to be booted before install, and simctl accepts "booted" only when exactly
# one is. Booting one that is already booted is not an error, so this is safe to repeat.
ensure_booted() {
  local udid
  udid="$(xcrun simctl list devices available | grep -m1 "^ *${DEVICE} (" | sed -E 's/.*\(([-0-9A-F]{36})\).*/\1/')"
  if [[ -z "${udid}" ]]; then
    echo "No available simulator named '${DEVICE}'. See: xcrun simctl list devices available" >&2
    exit 1
  fi

  xcrun simctl shutdown all >/dev/null 2>&1
  xcrun simctl boot "${udid}" >/dev/null 2>&1
  # bootstatus reports a non-zero status on a device it booted itself; it still blocks until
  # the device is up, which is the only reason it is here.
  xcrun simctl bootstatus "${udid}" -b >/dev/null 2>&1
  open -a Simulator
  echo "${udid}"
}

do_build() {
  if [[ ! -x "${UNITY_BIN}" ]]; then
    echo "Unity ${UNITY_VERSION} not found at ${UNITY_BIN}" >&2
    exit 127
  fi

  echo "Exporting the Xcode project (simulator SDK, arm64)..."
  rm -rf "${EXPORT_DIR}"
  "${UNITY_BIN}" -batchmode -quit -nographics \
    -projectPath "${ROOT}" \
    -executeMethod SheepGate.EditorTools.BuildScript.BuildIOSSimulator \
    -logFile "${ROOT}/Logs/ios-sim-export.log"
  if ! grep -q "\[Build\] Result Succeeded" "${ROOT}/Logs/ios-sim-export.log"; then
    echo "EXPORT FAILED — see Logs/ios-sim-export.log" >&2
    grep -E "\): error CS|Build failed" "${ROOT}/Logs/ios-sim-export.log" | head -10 >&2
    exit 1
  fi

  # IL2CPP compiles here, not in the Unity step, so this is the slow one: minutes, and the
  # first run for an architecture has no bee cache to hit.
  echo "Compiling with Xcode. This takes several minutes..."
  xcodebuild -project "${EXPORT_DIR}/Unity-iPhone.xcodeproj" \
    -scheme Unity-iPhone -configuration Debug \
    -sdk iphonesimulator -arch arm64 \
    -derivedDataPath "${DERIVED}" \
    CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO \
    build > "${ROOT}/Logs/ios-sim-xcodebuild.log" 2>&1

  if [[ ! -d "${PRODUCT}" ]]; then
    echo "XCODE BUILD FAILED — see Logs/ios-sim-xcodebuild.log" >&2
    grep -E "error:" "${ROOT}/Logs/ios-sim-xcodebuild.log" | head -10 >&2
    exit 1
  fi
  echo "Built ${PRODUCT}"
}

do_run() {
  if [[ ! -d "${PRODUCT}" ]]; then
    echo "Nothing built yet. Run: tools/ios-sim.sh build" >&2
    exit 1
  fi

  ensure_booted >/dev/null
  xcrun simctl install booted "${PRODUCT}" || exit 1

  # --console-pty keeps Unity's Debug.Log in the foreground stream, which is where the boot
  # health line lives. Backgrounded so the shell comes back while the game runs.
  local log="${ROOT}/Logs/ios-sim-console.log"
  xcrun simctl launch --console-pty booted "${BUNDLE_ID}" > "${log}" 2>&1 &

  for _ in $(seq 1 30); do
    grep -q "\[Boot\] Ready" "${log}" 2>/dev/null && break
    sleep 1
  done

  grep -E "^\[Boot\]" "${log}" || {
    echo "The player did not reach [Boot] Ready — see ${log}" >&2
    exit 1
  }
  echo
  echo "Running on ${DEVICE}. Console: ${log}"
}

do_shot() {
  local name="${SHOT_NAME:-shot}"
  local path="${ROOT}/Logs/${name}.png"
  xcrun simctl io booted screenshot "${path}" >/dev/null 2>&1 || {
    echo "No booted simulator to screenshot." >&2
    exit 1
  }
  echo "${path}"
}

case "${COMMAND}" in
  build) do_build ;;
  run)   do_run ;;
  shot)  do_shot ;;
  reset) xcrun simctl uninstall booted "${BUNDLE_ID}" && echo "Uninstalled — save and telemetry are gone." ;;
  all)   do_build && do_run ;;
esac
