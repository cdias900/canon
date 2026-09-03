#!/usr/bin/env bash
# Build, install and run the Android player on an emulator — the Android half of tools/ios-sim.sh.
#
# Usage:
#   tools/android-emu.sh                 build, boot, install, launch
#   tools/android-emu.sh setup           create the AVD this script uses (once per machine)
#   tools/android-emu.sh build           build the APK only
#   tools/android-emu.sh boot            boot the emulator and wait for Android
#   tools/android-emu.sh run             install the APK and launch it
#   tools/android-emu.sh shot [name]     write a screenshot to Logs/
#   tools/android-emu.sh log             the player's own log lines since launch
#   tools/android-emu.sh reset           uninstall, which clears the save
#   tools/android-emu.sh stop            shut the emulator down
#
#   tools/android-emu.sh tap X Y         tap a PIXEL (see below)
#   tools/android-emu.sh swipe X1 Y1 X2 Y2 [ms]
#   tools/android-emu.sh text "..."      type into whatever has the keyboard
#   tools/android-emu.sh key BACK        a keyevent name or number
#
# THREE THINGS THAT ARE NOT LIKE iOS
#
#   The image has to match the host. This Mac is arm64, and the three AVDs that were already on it
#   are x86 — the emulator will not run them here. `setup` creates one on an arm64-v8a image, and
#   that is the one this script boots. Nothing else is touched.
#
#   Coordinates are PIXELS, not points: the AVD is a Pixel 6 profile at 1080x2400, and a screenshot
#   is the same size, so read the number off the screenshot and tap it. (idb on iOS takes points;
#   adb takes what the framebuffer takes.) There is no accessibility tree for a Unity view here
#   either: tap a point, screenshot, look.
#
#   One adb. The Unity Android module ships a second SDK with its own platform-tools, and two adb
#   versions kill each other's server — which reads as the emulator vanishing mid-session. This
#   script uses the SDK below and only that one; do not mix in the one under the Unity install.
#
# WHAT CANNOT BE DONE HERE. The player gets no command line on Android, so the e2e runner cannot be
# driven and -table-url cannot be given; there is no PlayerPrefs door from adb the way `defaults
# write` is one on the simulator. This is tap-and-look for the solo game, which is what the
# definition of done asks for on Android.
set -uo pipefail

UNITY_VERSION="${UNITY_VERSION:-6000.3.23f1}"
UNITY_BIN="/Applications/Unity/Hub/Editor/${UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SDK="${ANDROID_SDK_ROOT:-${HOME}/Library/Android/sdk}"
ADB="${SDK}/platform-tools/adb"
EMULATOR="${SDK}/emulator/emulator"
AVDMANAGER="${SDK}/cmdline-tools/latest/bin/avdmanager"
SDKMANAGER="${SDK}/cmdline-tools/latest/bin/sdkmanager"
AVD="${AVD:-canon_arm64}"
IMAGE="system-images;android-35;google_apis;arm64-v8a"
DEVICE_PROFILE="pixel_6"
APK="${ROOT}/Builds/android/SheepGate.apk"
PACKAGE="com.createhack.portadasovelhas"
GPU="${GPU:-host}"

COMMAND="all"
ARGS=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    setup|build|boot|run|shot|log|reset|stop|all|tap|swipe|text|key) COMMAND="$1"; shift ;;
    *) ARGS+=("$1"); shift ;;
  esac
done

mkdir -p "${ROOT}/Logs"
export ANDROID_HOME="${SDK}"
export ANDROID_SDK_ROOT="${SDK}"

need() {
  if [[ ! -x "$1" ]]; then
    echo "Missing $1 — is the Android SDK at ${SDK}?" >&2
    exit 127
  fi
}

do_setup() {
  need "${SDKMANAGER}"; need "${AVDMANAGER}"
  if [[ ! -d "${SDK}/system-images/android-35/google_apis/arm64-v8a" ]]; then
    echo "Downloading the arm64 image (about a gigabyte)..."
    yes | "${SDKMANAGER}" --licenses >/dev/null 2>&1
    "${SDKMANAGER}" "${IMAGE}" || exit 1
  fi
  if "${AVDMANAGER}" list avd 2>/dev/null | grep -q "Name: ${AVD}$"; then
    echo "AVD ${AVD} already exists."
  else
    echo "Creating AVD ${AVD} (${DEVICE_PROFILE}, ${IMAGE})..."
    echo no | "${AVDMANAGER}" create avd -n "${AVD}" -k "${IMAGE}" -d "${DEVICE_PROFILE}" || exit 1
  fi
  echo "Ready. Taps go in pixels (1080x2400). See the header of this file."
}

booted() {
  "${ADB}" get-state >/dev/null 2>&1 && [[ "$("${ADB}" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" == "1" ]]
}

do_boot() {
  need "${EMULATOR}"; need "${ADB}"
  if booted; then
    echo "Emulator already up."
    return
  fi
  if ! "${EMULATOR}" -list-avds | grep -qx "${AVD}"; then
    echo "No AVD named ${AVD}. Run: tools/android-emu.sh setup" >&2
    exit 1
  fi
  echo "Booting ${AVD} (gpu ${GPU})..."
  nohup "${EMULATOR}" -avd "${AVD}" -no-audio -no-boot-anim -gpu "${GPU}" \
    > "${ROOT}/Logs/android-emulator.log" 2>&1 &
  "${ADB}" wait-for-device
  local tries=0
  until booted; do
    tries=$(( tries + 1 ))
    if [[ ${tries} -ge 120 ]]; then
      echo "Android did not finish booting in 4 minutes — see Logs/android-emulator.log" >&2
      exit 1
    fi
    sleep 2
  done
  "${ADB}" shell settings put global window_animation_scale 0 >/dev/null 2>&1
  "${ADB}" shell settings put global transition_animation_scale 0 >/dev/null 2>&1
  "${ADB}" shell settings put global animator_duration_scale 0 >/dev/null 2>&1
  echo "Booted."
}

do_build() {
  if [[ ! -x "${UNITY_BIN}" ]]; then
    echo "Unity ${UNITY_VERSION} not found at ${UNITY_BIN}" >&2
    exit 127
  fi
  echo "Building the APK (IL2CPP, ARM64). The first time switches the target and reimports; allow a while..."
  rm -f "${APK}"
  "${UNITY_BIN}" -batchmode -quit -nographics \
    -projectPath "${ROOT}" \
    -buildTarget Android \
    -executeMethod SheepGate.EditorTools.BuildScript.BuildAndroid \
    -logFile "${ROOT}/Logs/android-build.log"
  if ! grep -q "\[Build\] Result Succeeded" "${ROOT}/Logs/android-build.log" || [[ ! -f "${APK}" ]]; then
    echo "BUILD FAILED — see Logs/android-build.log" >&2
    grep -E "\): error CS|Build failed|error:|Exception" "${ROOT}/Logs/android-build.log" | head -12 >&2
    exit 1
  fi
  echo "Built ${APK} ($(du -h "${APK}" | cut -f1))"
}

do_run() {
  need "${ADB}"
  if [[ ! -f "${APK}" ]]; then
    echo "No APK at ${APK}. Run: tools/android-emu.sh build" >&2
    exit 1
  fi
  if ! booted; then
    do_boot
  fi
  echo "Installing..."
  "${ADB}" install -r "${APK}" >/dev/null || exit 1
  "${ADB}" logcat -c
  # The activity is resolved from the package at run time rather than spelled here: the class is
  # Unity's business and has changed between versions, and the package never does. (`monkey -p`
  # was the first attempt; on this emulator it returned without starting anything.)
  local activity
  activity="$("${ADB}" shell cmd package resolve-activity --brief \
    -a android.intent.action.MAIN -c android.intent.category.LAUNCHER "${PACKAGE}" | tail -1 | tr -d '\r')"
  if [[ -z "${activity}" || "${activity}" == *"No activity"* ]]; then
    echo "Could not resolve a launcher activity for ${PACKAGE}." >&2
    exit 1
  fi
  "${ADB}" shell am start -n "${activity}" >/dev/null 2>&1
  local tries=0
  until "${ADB}" logcat -d -s Unity:V 2>/dev/null | grep -q "\[Boot\] Ready"; do
    tries=$(( tries + 1 ))
    if [[ ${tries} -ge 60 ]]; then
      echo "The player did not reach [Boot] Ready in 2 minutes — see: tools/android-emu.sh log" >&2
      "${ADB}" logcat -d -s Unity:V AndroidRuntime:E 2>/dev/null | tail -30 >&2
      exit 1
    fi
    sleep 2
  done
  "${ADB}" logcat -d -s Unity:V | grep "\[Boot\]" | tail -3
}

do_shot() {
  need "${ADB}"
  local name="${ARGS[0]:-android}"
  local path="${ROOT}/Logs/${name}.png"
  "${ADB}" exec-out screencap -p > "${path}" 2>/dev/null || { echo "No emulator to screenshot." >&2; exit 1; }
  echo "${path}"
}

do_log() {
  need "${ADB}"
  "${ADB}" logcat -d -s Unity:V AndroidRuntime:E
}

do_reset() {
  need "${ADB}"
  "${ADB}" uninstall "${PACKAGE}" >/dev/null 2>&1 && echo "Uninstalled ${PACKAGE}." || echo "Nothing to uninstall."
}

do_stop() {
  need "${ADB}"
  "${ADB}" emu kill >/dev/null 2>&1 && echo "Emulator stopped." || echo "No emulator running."
}

do_input() {
  need "${ADB}"
  local needed=2
  case "${COMMAND}" in text|key) needed=1 ;; swipe) needed=4 ;; esac
  if [[ ${#ARGS[@]} -lt ${needed} ]]; then
    echo "${COMMAND} needs ${needed} argument(s). See the usage block at the top of this file." >&2
    exit 2
  fi
  case "${COMMAND}" in
    tap)   "${ADB}" shell input tap "${ARGS[0]}" "${ARGS[1]}" ;;
    swipe) "${ADB}" shell input swipe "${ARGS[0]}" "${ARGS[1]}" "${ARGS[2]}" "${ARGS[3]}" "${ARGS[4]:-300}" ;;
    text)  "${ADB}" shell input text "$(printf '%s' "${ARGS[0]}" | sed 's/ /%s/g')" ;;
    key)   "${ADB}" shell input keyevent "${ARGS[0]}" ;;
  esac
}

case "${COMMAND}" in
  setup) do_setup ;;
  build) do_build ;;
  boot)  do_boot ;;
  run)   do_run ;;
  shot)  do_shot ;;
  log)   do_log ;;
  reset) do_reset ;;
  stop)  do_stop ;;
  tap|swipe|text|key) do_input ;;
  all)   do_build; do_boot; do_run ;;
esac
