#!/usr/bin/env bash
# Build, install and run the game on a REAL iPhone over USB.
#
# The sibling script tools/ios-sim.sh does the same for the simulator and is what the e2e run and
# every day-to-day check uses. This one exists for the things a simulator cannot answer: how the
# game feels in a hand, what the real touch targets are like, whether the frame rate holds on the
# device rather than on a Mac pretending to be one.
#
# The difference that matters is signing. A simulator build needs none, which is why ios-sim.sh
# passes CODE_SIGNING_ALLOWED=NO. A device build must be signed by a real team, and Unity REGENERATES
# the Xcode project on every export — so setting the team inside Xcode fixes one build and is gone
# the next time. It lives in ProjectSettings instead:
#
#     appleEnableAutomaticSigning: 1
#     appleDeveloperTeamID: <your team>
#
# Both are committed, so this works on a fresh clone for anyone on the same team.
#
# Usage:
#   tools/ios-device.sh                 export, compile, install, launch
#   tools/ios-device.sh build           export and compile only
#   tools/ios-device.sh run             install and launch on the connected device
#   tools/ios-device.sh devices         list what is plugged in and usable
#   tools/ios-device.sh log             stream the game's own log lines from the device
#
#   --device "<name or udid>"           which iPhone (default: the one connected device)
#
# FIRST RUN, on the phone itself: Settings > General > VPN & Device Management > trust the
# developer certificate. An untrusted certificate installs fine and then refuses to launch, and the
# error names neither the setting nor the certificate.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_VERSION="${UNITY_VERSION:-6000.3.23f1}"
UNITY_BIN="${UNITY_BIN:-/Applications/Unity/Hub/Editor/${UNITY_VERSION}/Unity.app/Contents/MacOS/Unity}"
EXPORT_DIR="${ROOT}/Builds/ios"
DERIVED="${EXPORT_DIR}/DerivedData"
mkdir -p "${ROOT}/Logs"

DEVICE=""
CMD="all"
ARGS=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --device) DEVICE="$2"; shift 2 ;;
    build|run|devices|log|all) CMD="$1"; shift ;;
    *) ARGS+=("$1"); shift ;;
  esac
done

# The bundle id and the .app name both come from PlayerSettings and both have changed under this
# repo before, so neither is spelled here. A hardcoded product name once turned a successful build
# into "the player did not reach Ready", because the script was looking for a bundle Xcode had
# stopped producing.
resolve_product() {
  find "${DERIVED}/Build/Products" -maxdepth 2 -name "*.app" -type d 2>/dev/null | head -1
}

resolve_bundle_id() {
  local product; product="$(resolve_product)"
  [[ -z "${product}" ]] && return 1
  /usr/libexec/PlistBuddy -c "Print :CFBundleIdentifier" "${product}/Info.plist" 2>/dev/null
}

# One connected device is the overwhelmingly common case, so it is the default. Anything else has to
# be named, rather than guessed at: installing a build on the wrong phone is confusing in a way that
# takes a while to notice.
resolve_device() {
  # The identifier is matched by SHAPE, not by column. The table's model column has a variable
  # number of words ("iPhone 17 Pro Max (iPhone18,2)"), so any positional field lands somewhere
  # different depending on which phone is plugged in — on this one, $(NF-3) is the string "17".
  local uuid='[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}'
  if [[ -n "${DEVICE}" ]]; then
    xcrun devicectl list devices 2>/dev/null \
      | grep -E "connected" | grep -F "${DEVICE}" | grep -oE "${uuid}" | head -1
    return
  fi
  local found
  found="$(xcrun devicectl list devices 2>/dev/null | grep -E 'connected' | grep -oE "${uuid}")"
  local count; count="$(echo "${found}" | grep -c . || true)"
  if [[ "${count}" -gt 1 ]]; then
    echo "More than one device is connected. Name one with --device." >&2
    xcrun devicectl list devices 2>/dev/null | awk '/connected/' >&2
    exit 1
  fi
  echo "${found}"
}

do_devices() {
  xcrun devicectl list devices 2>/dev/null || {
    echo "devicectl is unavailable. Xcode 15 or newer is needed for device installs." >&2
    exit 127
  }
}

do_build() {
  if [[ ! -x "${UNITY_BIN}" ]]; then
    echo "Unity ${UNITY_VERSION} not found at ${UNITY_BIN}" >&2
    exit 127
  fi

  local team
  team="$(awk '/appleDeveloperTeamID:/ {print $2}' "${ROOT}/ProjectSettings/ProjectSettings.asset")"
  if [[ -z "${team}" ]]; then
    echo "No signing team in ProjectSettings." >&2
    echo "  Set appleDeveloperTeamID and appleEnableAutomaticSigning: 1, or in the Unity editor:" >&2
    echo "  Player Settings > iOS > Identification > Automatically Sign, then pick your team." >&2
    echo "  Your team id is the code in brackets in: security find-identity -v -p codesigning" >&2
    exit 1
  fi

  echo "Exporting the Xcode project (device SDK, team ${team})..."
  rm -rf "${EXPORT_DIR}"
  "${UNITY_BIN}" -batchmode -quit -nographics \
    -projectPath "${ROOT}" \
    -executeMethod SheepGate.EditorTools.BuildScript.BuildIOS \
    -logFile "${ROOT}/Logs/ios-device-export.log"
  if ! grep -q "\[Build\] Result Succeeded" "${ROOT}/Logs/ios-device-export.log"; then
    echo "EXPORT FAILED — see Logs/ios-device-export.log" >&2
    grep -E "\): error CS|Build failed" "${ROOT}/Logs/ios-device-export.log" | head -10 >&2
    exit 1
  fi

  # IL2CPP compiles here rather than in the Unity step, so this is the slow one — minutes, and the
  # first run for an architecture has no bee cache to hit. -allowProvisioningUpdates is what lets
  # Xcode register the device and mint a profile without a trip through the developer portal.
  echo "Compiling and signing with Xcode. This takes several minutes..."
  xcodebuild -project "${EXPORT_DIR}/Unity-iPhone.xcodeproj" \
    -scheme Unity-iPhone -configuration Debug \
    -sdk iphoneos -arch arm64 \
    -derivedDataPath "${DERIVED}" \
    -allowProvisioningUpdates \
    DEVELOPMENT_TEAM="${team}" \
    CODE_SIGN_STYLE=Automatic \
    build > "${ROOT}/Logs/ios-device-xcodebuild.log" 2>&1 || true

  local product; product="$(resolve_product)"
  if [[ -z "${product}" ]]; then
    echo "XCODE BUILD FAILED — see Logs/ios-device-xcodebuild.log" >&2
    # Signing failures are the common case here and they are wordy, so the two lines that actually
    # say what to do are surfaced ahead of the generic ones.
    grep -E "requires a provisioning profile|no profiles for|Signing for|error:" \
      "${ROOT}/Logs/ios-device-xcodebuild.log" | head -10 >&2
    exit 1
  fi
  echo "Built ${product}"
}

do_run() {
  local product; product="$(resolve_product)"
  if [[ -z "${product}" ]]; then
    echo "Nothing built yet. Run: tools/ios-device.sh build" >&2
    exit 1
  fi

  local device; device="$(resolve_device)"
  if [[ -z "${device}" ]]; then
    echo "No connected iPhone. Plug one in, unlock it, and tap Trust if asked." >&2
    echo "  tools/ios-device.sh devices   shows what the Mac can see" >&2
    exit 1
  fi

  local bundle; bundle="$(resolve_bundle_id)" || true
  echo "Installing on ${device}..."
  xcrun devicectl device install app --device "${device}" "${product}" >/dev/null || {
    echo "INSTALL FAILED. If the phone says the developer is untrusted, trust it in" >&2
    echo "  Settings > General > VPN & Device Management, then run this again." >&2
    exit 1
  }

  if [[ -n "${bundle}" ]]; then
    echo "Launching ${bundle}..."
    xcrun devicectl device process launch --device "${device}" "${bundle}" >/dev/null || {
      echo "Installed, but the launch was refused — usually an untrusted developer certificate." >&2
      echo "  Settings > General > VPN & Device Management on the phone, then open it by hand." >&2
      exit 1
    }
  fi
  echo "Running on ${device}. Logs: tools/ios-device.sh log"
}

# The device equivalent of reading the simulator console. Filtered to the game's own lines, because
# unfiltered device logs are a firehose.
do_log() {
  local device; device="$(resolve_device)"
  [[ -z "${device}" ]] && { echo "No connected iPhone." >&2; exit 1; }
  echo "Streaming. Ctrl-C to stop."
  xcrun devicectl device console --device "${device}" 2>/dev/null \
    | grep --line-buffered -E "\[Boot\]|\[World\]|\[HUD\]|\[Backpack|\[Wardrobe|Exception|Error"
}

case "${CMD}" in
  devices) do_devices ;;
  build)   do_build ;;
  run)     do_run ;;
  log)     do_log ;;
  all)     do_build; do_run ;;
esac
