#!/usr/bin/env bash
set -euo pipefail

INSTALL_DIR="$HOME/.local/lib/office-web-launcher"
APPLICATIONS_DIR="$HOME/.local/share/applications"
CONFIG_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/office-web-launcher"
DESKTOP_FILE="$APPLICATIONS_DIR/office-web-launcher.desktop"
BACKUP_FILE="$CONFIG_DIR/previous-mime-associations.tsv"

if [[ -f "$BACKUP_FILE" ]] && command -v xdg-mime >/dev/null 2>&1; then
  while IFS=$'\t' read -r mime previous; do
    if [[ -n "$mime" && -n "$previous" ]]; then
      xdg-mime default "$previous" "$mime" || true
    fi
  done < "$BACKUP_FILE"
fi

rm -f -- "$DESKTOP_FILE"
rm -f -- "$INSTALL_DIR/OfficeWebLauncher" "$INSTALL_DIR/OfficeWebLauncher.json" "$INSTALL_DIR/Uninstall.sh"
rmdir -- "$INSTALL_DIR" 2>/dev/null || true
rm -f -- "$BACKUP_FILE"
rmdir -- "$CONFIG_DIR/credentials" 2>/dev/null || true
rmdir -- "$CONFIG_DIR" 2>/dev/null || true

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$APPLICATIONS_DIR" >/dev/null 2>&1 || true
fi

echo "Office Web Launcher was removed. Uploaded OneDrive files were left in place."
