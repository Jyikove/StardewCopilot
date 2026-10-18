#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
build_game_path="${GAME_PATH:-$HOME/Library/Application Support/Steam/steamapps/common/Stardew Valley/Contents/MacOS}"
build_package=false
build_has_path=false
for build_argument in "$@"; do
  case "$build_argument" in
    --package) build_package=true ;;
    --help|-h) echo 'Usage: ./build.sh [game-directory] [--package]'; exit 0 ;;
    --*) echo "Unknown option: $build_argument" >&2; exit 1 ;;
    *)
      if [[ "$build_has_path" == true ]]; then echo 'Only one game directory may be supplied.' >&2; exit 1; fi
      build_game_path="$build_argument"; build_has_path=true ;;
  esac
done
build_dotnet="${DOTNET_BIN:-dotnet}"
"$build_dotnet" build src/StardewCodexChat.csproj -c Release --nologo -p:DebugType=None "-p:PathMap=$(pwd)=/_/StardewCopilot" "-p:GamePath=$build_game_path"
mkdir -p dist/StardewCopilot
cp src/bin/Release/net6.0/StardewCopilot.dll dist/StardewCopilot/
cp src/bin/Release/net6.0/HtmlAgilityPack.dll dist/StardewCopilot/
cp src/manifest.json dist/StardewCopilot/
cp README.md dist/StardewCopilot/
cp TESTING.md dist/StardewCopilot/
rm -f dist/StardewCopilot/StardewCopilotKeySetup "dist/StardewCopilot/配置 DeepSeek Key.command"
echo "Built: $(pwd)/dist/StardewCopilot"
if [[ "$build_package" == true ]]; then
  build_version="$(python3 -c 'import json; print(json.load(open("src/manifest.json"))["Version"])')"
  python3 tools/package.py dist/StardewCopilot "dist/StardewCopilot-$build_version-macOS.zip"
  echo "Packaged: $(pwd)/dist/StardewCopilot-$build_version-macOS.zip"
fi
