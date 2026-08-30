#!/usr/bin/env bash
# Build, install and run the iOS player on a simulator.
#
# A Unity iOS build is an Xcode project, not an app, so getting to something runnable takes
# three steps: export from Unity, compile with Xcode, install on a booted simulator. This
# wraps all three because the two settings that make it work are not discoverable from any
# error message — see BuildScript.BuildIOSSimulator for the architecture trap.
#
# Input is driven through idb, which injects events the way a real device does, so nothing
# on the Mac is touched: the cursor does not move, focus stays where the user left it, and the
# Simulator window can be hidden behind everything else the whole time. See INPUT below.
#
# Usage:
#   tools/ios-sim.sh                 export, compile, install, launch
#   tools/ios-sim.sh build           export and compile only
#   tools/ios-sim.sh run             install and launch on the booted simulator
#   tools/ios-sim.sh shot [name]     write a screenshot to Logs/
#   tools/ios-sim.sh reset           uninstall, which clears the save and the telemetry
#
#   tools/ios-sim.sh setup           install the input tooling (once per machine)
#   tools/ios-sim.sh tap X Y         tap a device point
#   tools/ios-sim.sh press X Y [S]   press and hold for S seconds (default 0.8)
#   tools/ios-sim.sh swipe X1 Y1 X2 Y2 [S]
#   tools/ios-sim.sh text "..."      type into whatever has the keyboard
#   tools/ios-sim.sh key 40          a HID keycode (40 = Return)
#   tools/ios-sim.sh udid            the booted device's udid
#
#   --device "iPhone 17 Pro"         which simulator to boot (default below)
#
# INPUT — why idb and not AppleScript
#
#   `xcrun simctl` has no tap at all, and the obvious substitute,
#   `osascript -e 'tell application "System Events" to click at {x, y}'`, is worse than
#   nothing here: it reports success and does nothing, because the Simulator's Metal view
#   ignores synthetic accessibility clicks. A run that uses it looks exactly like a frozen
#   game. Posting a real CGEvent does work, but only by moving the physical cursor and
#   raising the Simulator, which makes the machine unusable while a session plays.
#
#   idb injects through IndigoHID — the same path a real device uses — so it needs neither
#   the cursor nor focus nor a visible window. It is the iOS equivalent of `adb shell input`.
#
#   Coordinates are DEVICE POINTS (iPhone 17 Pro is 402x874), origin top-left. They do not
#   depend on where the Simulator window is or how big it is, which is the other thing the
#   cursor-driven approach kept getting wrong.
#
#   What does NOT port from a web app: `idb ui describe-all` and `describe-point` return
#   nothing useful here. Unity draws into one Metal view and publishes no accessibility
#   tree, so `describe-all` reports a single node for the whole application and
#   `describe-point` reports none. There is no finding a button by its label: tap a point,
#   then screenshot to see what happened. Element-level assertions belong in tools/e2e.sh,
#   which drives the real EventSystem from inside the build and can see the hierarchy.
set -uo pipefail

UNITY_VERSION="6000.3.23f1"
UNITY_BIN="/Applications/Unity/Hub/Editor/${UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXPORT_DIR="${ROOT}/Builds/ios-sim"
DERIVED="${EXPORT_DIR}/DerivedData"
PRODUCT="${DERIVED}/Build/Products/Debug-iphonesimulator/PortadasOvelhas.app"
BUNDLE_ID="com.createhack.portadasovelhas"
DEVICE="iPhone 17 Pro"

# Deliberately not under /tmp: the venv that the first version of this lived in was cleaned
# up between sessions, and the tool then failed in a way that read as "idb is broken".
IDB_VENV="${IDB_VENV:-${HOME}/.cache/create-hack/idbvenv}"
IDB="${IDB_VENV}/bin/idb"

COMMAND="all"
ARGS=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --device) DEVICE="${2:-}"; shift 2 ;;
    build|run|shot|reset|all|setup|tap|press|swipe|text|key|udid) COMMAND="$1"; shift ;;
    *) ARGS+=("$1"); SHOT_NAME="$1"; shift ;;
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
  # -g leaves it behind whatever the user is doing. The window is only ever needed to look
  # at; input goes through idb and screenshots come from the framebuffer.
  open -g -a Simulator
  echo "${udid}"
}

# ------------------------------------------------------------------ input, through idb

booted_udid() {
  xcrun simctl list devices booted -j | python3 -c 'import sys, json
d = json.load(sys.stdin)
ids = [x["udid"] for v in d["devices"].values() for x in v if x.get("state") == "Booted"]
print(ids[0] if ids else "")'
}

do_setup() {
  if ! command -v /opt/homebrew/bin/idb_companion >/dev/null 2>&1; then
    echo "Installing idb-companion..."
    brew tap facebook/fb 2>/dev/null
    brew install idb-companion || exit 1
  fi

  local python
  python="$(command -v python3.12 || command -v /opt/homebrew/bin/python3.12 || true)"
  if [[ -z "${python}" ]]; then
    echo "fb-idb needs Python 3.12: brew install python@3.12" >&2
    exit 1
  fi

  if [[ ! -x "${IDB}" ]]; then
    echo "Creating ${IDB_VENV}..."
    mkdir -p "$(dirname "${IDB_VENV}")"
    "${python}" -m venv "${IDB_VENV}" || exit 1
    "${IDB_VENV}/bin/pip" -q install fb-idb || exit 1
  fi

  # Prove the channel rather than announcing it. describe-all is read-only, and on this app it
  # is also the demonstration that there is no accessibility tree to search: one node, the
  # application itself. If that is what comes back, input works and find-by-label never will.
  local udid
  udid="$(booted_udid)"
  if [[ -n "${udid}" ]]; then
    export PATH="/opt/homebrew/bin:${PATH}"
    "${IDB}" connect "${udid}" >/dev/null 2>&1 || true
    if "${IDB}" ui describe-all --udid "${udid}" >/dev/null 2>&1; then
      echo "Input tooling ready and talking to ${udid}."
    else
      echo "Installed, but idb could not reach ${udid}. Try: tools/ios-sim.sh run" >&2
      exit 1
    fi
  else
    echo "Input tooling installed. Boot a device to use it: tools/ios-sim.sh run"
  fi

  echo "Taps go in device points. See the INPUT block at the top of this file."
}

# Resolves idb and the booted device, and connects. Connecting is idempotent and the
# companion outlives the call, so only the first one in a session pays for it.
ensure_idb() {
  if [[ ! -x "${IDB}" ]]; then
    echo "The input tooling is not installed. Run: tools/ios-sim.sh setup" >&2
    exit 127
  fi

  IDB_UDID="$(booted_udid)"
  if [[ -z "${IDB_UDID}" ]]; then
    echo "No booted simulator. Run: tools/ios-sim.sh run" >&2
    exit 1
  fi

  export IDB_UDID
  export PATH="/opt/homebrew/bin:${PATH}"
  "${IDB}" connect "${IDB_UDID}" >/dev/null 2>&1 || true
}

do_input() {
  local needed=2
  case "${COMMAND}" in text|key) needed=1 ;; swipe) needed=4 ;; esac
  if [[ ${#ARGS[@]} -lt ${needed} ]]; then
    echo "${COMMAND} needs ${needed} argument(s). See the usage block at the top of this file." >&2
    exit 2
  fi

  ensure_idb
  case "${COMMAND}" in
    tap)   "${IDB}" ui tap "${ARGS[0]}" "${ARGS[1]}" ;;
    press) "${IDB}" ui tap --duration "${ARGS[2]:-0.8}" "${ARGS[0]}" "${ARGS[1]}" ;;
    swipe) "${IDB}" ui swipe --duration "${ARGS[4]:-0.3}" "${ARGS[0]}" "${ARGS[1]}" "${ARGS[2]}" "${ARGS[3]}" ;;
    text)  "${IDB}" ui text "${ARGS[0]}" ;;
    key)   "${IDB}" ui key "${ARGS[0]}" ;;
  esac
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

  # Said here rather than at the first tap, because the first tap is usually mid-playthrough.
  if [[ ! -x "${IDB}" ]]; then
    echo "To drive it without taking over the mouse: tools/ios-sim.sh setup"
  fi
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
  setup) do_setup ;;
  udid)  booted_udid ;;
  tap|press|swipe|text|key) do_input ;;
esac
