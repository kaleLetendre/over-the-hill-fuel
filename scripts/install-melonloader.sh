#!/bin/sh
# Install the MelonLoader mod loader into Over the Hill (Steam, Proton).
# Pinned to v0.7.3, the version the game's other mods (e.g. TriggerFX) target.
# Downloads into build/ if missing, then unpacks into the game folder.
# Adds files only; remove them with scripts/uninstall-melonloader.sh.
#
# After installing, set the game's Steam launch option (Properties > General):
#   WINEDLLOVERRIDES="version=n,b" %command%
# The first launch takes a few minutes while MelonLoader generates the
# Il2CppAssemblies the mod builds against.

# Stop at the first failed step, so a half-unpacked install doesn't go unnoticed.
set -e

cd "$(dirname "$0")/.."
. ./scripts/paths.sh

ML_URL=https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.x64.zip
ZIP=build/MelonLoader-v0.7.3.x64.zip

mkdir -p build
if [ ! -f "$ZIP" ]; then
    curl -sSfL -o "$ZIP" "$ML_URL"
fi

if [ ! -d "$GAME_DIR" ]; then
    echo "game not found at $GAME_DIR (set GAME_DIR)" >&2
    exit 1
fi

unzip -o -q "$ZIP" -d "$GAME_DIR"
mkdir -p "$GAME_DIR/Mods"
echo "MelonLoader installed in $GAME_DIR"
echo 'Steam launch option: WINEDLLOVERRIDES="version=n,b" %command%'
