#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
install_game_path="${1:-${GAME_PATH:-$HOME/Library/Application Support/Steam/steamapps/common/Stardew Valley/Contents/MacOS}}"
if [[ ! -f "$install_game_path/StardewModdingAPI.dll" ]]; then
  echo "SMAPI not found. Pass the directory containing StardewModdingAPI.dll." >&2
  exit 1
fi
if [[ ! -f dist/StardewCopilot/StardewCopilot.dll ]]; then
  echo "Run ./build.sh first." >&2
  exit 1
fi
if pgrep -f '[/]StardewModdingAPI|[/]Stardew Valley.dll' >/dev/null; then
  echo "Please close Stardew Valley before installing." >&2
  exit 1
fi
mkdir -p "$install_game_path/Mods/StardewCopilot"
cp dist/StardewCopilot/HtmlAgilityPack.dll dist/StardewCopilot/StardewCopilot.dll dist/StardewCopilot/manifest.json dist/StardewCopilot/README.md dist/StardewCopilot/TESTING.md "$install_game_path/Mods/StardewCopilot/"
rm -f "$install_game_path/Mods/StardewCopilot/StardewCopilotKeySetup" "$install_game_path/Mods/StardewCopilot/配置 DeepSeek Key.command"
echo "Installed: $install_game_path/Mods/StardewCopilot"
