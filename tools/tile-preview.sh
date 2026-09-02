#!/usr/bin/env bash
# Render the game's procedural tile art to a PNG, without Unity.
#
# WHY THIS EXISTS. Every tile in this game is drawn in C# at runtime, so the only way to see one
# used to be a full build: Unity export, Xcode compile, install, launch, screenshot. Minutes per
# glance, for art you change ten times in a row.
#
# The art layer does not actually need Unity. ArtPalette, PixelCanvas, ValueNoise and TileArt are
# pure computation over a byte buffer, so this compiles THE REAL FILES — not copies — against a
# small UnityEngine stub, using the Roslyn that ships inside Unity, and writes a PNG in seconds.
# Because it compiles the shipping sources, what you see is what the game draws; if it drifts, the
# build breaks rather than lying to you.
#
# It earned its place: a rubble field that a full device build reported as fine was shown by this
# harness to be a checkerboard of hard-edged squares, and the fix was measured here before it was
# ever compiled for a phone. See docs/development-guidelines.md section 3.
#
# Usage:
#   tools/tile-preview.sh sheet              every tile, laid out side by side
#   tools/tile-preview.sh zoom               the same at 5x, for judging pixels
#   tools/tile-preview.sh field [density]    a field of ruin tiles at 0..1 density (default 0.45),
#                                            which is how you tell scattered stone from wallpaper
#   tools/tile-preview.sh check              assert every pixel is in the world palette and opaque
#
# Output goes to Logs/tile-preview/ and is gitignored.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_VERSION="${UNITY_VERSION:-6000.3.23f1}"
UNITY_ROOT="${UNITY_ROOT:-/Applications/Unity/Hub/Editor/${UNITY_VERSION}/Unity.app/Contents/Resources/Scripting}"
DOTNET="${UNITY_ROOT}/NetCoreRuntime/dotnet"
CSC="${UNITY_ROOT}/DotNetSdkRoslyn/csc.dll"
SRC="${ROOT}/tools/tile-preview"
OUT="${ROOT}/Logs/tile-preview"

if [[ ! -x "${DOTNET}" ]]; then
  echo "No dotnet inside Unity ${UNITY_VERSION} at ${DOTNET}." >&2
  echo "Set UNITY_VERSION or UNITY_ROOT to the Unity you have installed." >&2
  exit 1
fi

mkdir -p "${OUT}"

# The reference set is DERIVED, never checked in: it is 165 absolute paths into a specific Unity
# install, and a committed copy would work on exactly one machine.
FRAMEWORK="$(ls -d "${UNITY_ROOT}/NetCoreRuntime/shared/Microsoft.NETCore.App/"*/ | head -1)"
FRAMEWORK="${FRAMEWORK%/}"
VERSION="$(basename "${FRAMEWORK}")"
REFS="${OUT}/refs.rsp"
: > "${REFS}"
for dll in "${FRAMEWORK}"/*.dll; do
  case "$(basename "${dll}")" in
    *.Native.dll|*.resources.dll) continue ;;
  esac
  printf -- '-r:"%s"\n' "${dll}" >> "${REFS}"
done

cat > "${OUT}/run.runtimeconfig.json" <<JSON
{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"${VERSION}"}}}
JSON

# The four art files are compiled from Assets/, not copied here. That is the whole point.
ART=(
  "${ROOT}/Assets/Scripts/Art/ArtPalette.cs"
  "${ROOT}/Assets/Scripts/Art/PixelCanvas.cs"
  "${ROOT}/Assets/Scripts/Art/ValueNoise.cs"
  "${ROOT}/Assets/Scripts/Art/TileArt.cs"
)

build() {
  local entry="$1" name="$2"
  "${DOTNET}" "${CSC}" -nologo -nostdlib -langversion:9 -target:exe \
    -out:"${OUT}/${name}.dll" "@${REFS}" \
    "${SRC}/UnityStub.cs" "${SRC}/${entry}" "${ART[@]}"
  cp "${OUT}/run.runtimeconfig.json" "${OUT}/${name}.runtimeconfig.json"
}

render() {
  local name="$1"; shift
  local dims
  dims="$("${DOTNET}" "${OUT}/${name}.dll" "${OUT}/${name}.raw" "$@")"
  node "${SRC}/png.mjs" "${OUT}/${name}.raw" "${OUT}/${name}.png" ${dims}
}

case "${1:-sheet}" in
  sheet) build Sheet.cs sheet; render sheet ;;
  zoom)  build Zoom.cs  zoom;  render zoom ;;
  field) build Field.cs field; render field "${2:-0.45}" ;;
  check)
    build Check.cs check
    "${DOTNET}" "${OUT}/check.dll"
    ;;
  *) echo "Usage: tools/tile-preview.sh [sheet|zoom|field <density>|check]" >&2; exit 2 ;;
esac
