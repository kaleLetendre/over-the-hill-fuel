#!/bin/sh
# Remove MelonLoader from Over the Hill. Leaves Mods/, Plugins/ and UserData/
# (your mods and their settings) in place; delete those by hand if wanted.
# Also remove the WINEDLLOVERRIDES launch option in Steam afterwards.

cd "$(dirname "$0")/.."
. ./scripts/paths.sh

rm -rf "$GAME_DIR/MelonLoader"
rm -f "$GAME_DIR/version.dll"
echo "MelonLoader removed from $GAME_DIR"
