#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
RUNTIME="${1:-linux-x64}"
case "$RUNTIME" in
  linux-x64|linux-arm64) ;;
  *) echo "Usage: ./Build.sh [linux-x64|linux-arm64]" >&2; exit 1 ;;
esac

OUTPUT="$SCRIPT_DIR/dist/$RUNTIME"
dotnet publish "$SCRIPT_DIR/OfficeWebLauncher.csproj" -c Release -r "$RUNTIME" --self-contained true -o "$OUTPUT"
install -m 755 "$SCRIPT_DIR/Setup.sh" "$OUTPUT/Setup.sh"
install -m 755 "$SCRIPT_DIR/Uninstall.sh" "$OUTPUT/Uninstall.sh"
install -m 644 "$SCRIPT_DIR/README.md" "$OUTPUT/README.md"
