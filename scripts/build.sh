#!/bin/sh
# Build the mod and copy it into the game's Mods folder.
#   scripts/build.sh            build + deploy
#   scripts/build.sh --no-copy  build only (DLL in build/out/)
# Needs the .NET SDK (8+) on PATH or in ~/.dotnet, and MelonLoader's first run
# done (MelonLoader/Il2CppAssemblies must exist).

# Stop at the first failed step, so a failed build is never deployed.
set -e

cd "$(dirname "$0")/.."
. ./scripts/paths.sh

if ! command -v dotnet > /dev/null && [ -x "$HOME/.dotnet/dotnet" ]; then
    export DOTNET_ROOT="$HOME/.dotnet"
    PATH="$HOME/.dotnet:$PATH"
fi

if [ ! -d "$GAME_DIR/MelonLoader/Il2CppAssemblies" ]; then
    echo "MelonLoader/Il2CppAssemblies missing: install MelonLoader and run the game once" >&2
    exit 1
fi

dotnet build src/OTH.Fuel.csproj -c Release -o build/out -p:GameDir="$GAME_DIR" -nologo -v quiet

if [ "$1" != "--no-copy" ]; then
    cp build/out/OTH.Fuel.dll "$GAME_DIR/Mods/"
    echo "deployed to $GAME_DIR/Mods/OTH.Fuel.dll"
fi
