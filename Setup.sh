#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
SOURCE_EXE="$SCRIPT_DIR/OfficeWebLauncher"
CLIENT_ID="${1:-}"
TENANT="${2:-common}"

if [[ ! -f "$SOURCE_EXE" ]]; then
  echo "OfficeWebLauncher must be in the same folder as Setup.sh." >&2
  exit 1
fi
if [[ -z "$CLIENT_ID" ]]; then
  read -r -p "Microsoft Entra Application (client) ID: " CLIENT_ID
fi
if [[ ! "$CLIENT_ID" =~ ^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$ ]]; then
  echo "The client ID must be a GUID from a Microsoft Entra app registration." >&2
  exit 1
fi
if [[ "$TENANT" != "common" && "$TENANT" != "organizations" && "$TENANT" != "consumers" ]]; then
  echo "Tenant must be common, organizations, or consumers." >&2
  exit 1
fi
if ! command -v xdg-mime >/dev/null 2>&1 || ! command -v xdg-open >/dev/null 2>&1; then
  echo "xdg-utils is required. On Ubuntu: sudo apt install xdg-utils" >&2
  exit 1
fi

INSTALL_DIR="$HOME/.local/lib/office-web-launcher"
APPLICATIONS_DIR="$HOME/.local/share/applications"
CONFIG_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/office-web-launcher"
DESKTOP_FILE="$APPLICATIONS_DIR/office-web-launcher.desktop"
BACKUP_FILE="$CONFIG_DIR/previous-mime-associations.tsv"

mkdir -p "$INSTALL_DIR" "$APPLICATIONS_DIR" "$CONFIG_DIR"
chmod 700 "$CONFIG_DIR"
install -m 755 "$SOURCE_EXE" "$INSTALL_DIR/OfficeWebLauncher"
install -m 755 "$SCRIPT_DIR/Uninstall.sh" "$INSTALL_DIR/Uninstall.sh"
printf '{\n  "clientId": "%s",\n  "tenant": "%s"\n}\n' "$CLIENT_ID" "$TENANT" > "$INSTALL_DIR/OfficeWebLauncher.json"
chmod 600 "$INSTALL_DIR/OfficeWebLauncher.json"

cat > "$DESKTOP_FILE" <<EOF
[Desktop Entry]
Type=Application
Name=Office Web Launcher
Comment=Open Office files in Microsoft 365 for the web
Exec="$INSTALL_DIR/OfficeWebLauncher" %f
Icon=x-office-document
Terminal=true
NoDisplay=false
MimeType=application/msword;application/vnd.openxmlformats-officedocument.wordprocessingml.document;application/vnd.openxmlformats-officedocument.wordprocessingml.template;application/vnd.ms-word.document.macroEnabled.12;application/vnd.ms-word.template.macroEnabled.12;application/vnd.oasis.opendocument.text;application/rtf;text/rtf;application/vnd.ms-excel;application/vnd.openxmlformats-officedocument.spreadsheetml.sheet;application/vnd.openxmlformats-officedocument.spreadsheetml.template;application/vnd.ms-excel.sheet.macroEnabled.12;application/vnd.ms-excel.sheet.binary.macroEnabled.12;application/vnd.ms-excel.template.macroEnabled.12;text/csv;application/vnd.oasis.opendocument.spreadsheet;application/vnd.ms-powerpoint;application/vnd.openxmlformats-officedocument.presentationml.presentation;application/vnd.openxmlformats-officedocument.presentationml.slideshow;application/vnd.openxmlformats-officedocument.presentationml.template;application/vnd.ms-powerpoint.presentation.macroEnabled.12;application/vnd.ms-powerpoint.slideshow.macroEnabled.12;application/vnd.ms-powerpoint.template.macroEnabled.12;application/vnd.oasis.opendocument.presentation;application/vnd.visio;
Categories=Office;
EOF
chmod 644 "$DESKTOP_FILE"

MIME_TYPES=(
  application/msword
  application/vnd.openxmlformats-officedocument.wordprocessingml.document
  application/vnd.openxmlformats-officedocument.wordprocessingml.template
  application/vnd.ms-word.document.macroEnabled.12
  application/vnd.ms-word.template.macroEnabled.12
  application/vnd.oasis.opendocument.text
  application/rtf text/rtf
  application/vnd.ms-excel
  application/vnd.openxmlformats-officedocument.spreadsheetml.sheet
  application/vnd.openxmlformats-officedocument.spreadsheetml.template
  application/vnd.ms-excel.sheet.macroEnabled.12
  application/vnd.ms-excel.sheet.binary.macroEnabled.12
  application/vnd.ms-excel.template.macroEnabled.12
  text/csv
  application/vnd.oasis.opendocument.spreadsheet
  application/vnd.ms-powerpoint
  application/vnd.openxmlformats-officedocument.presentationml.presentation
  application/vnd.openxmlformats-officedocument.presentationml.slideshow
  application/vnd.openxmlformats-officedocument.presentationml.template
  application/vnd.ms-powerpoint.presentation.macroEnabled.12
  application/vnd.ms-powerpoint.slideshow.macroEnabled.12
  application/vnd.ms-powerpoint.template.macroEnabled.12
  application/vnd.oasis.opendocument.presentation
  application/vnd.visio
)

touch "$BACKUP_FILE"
chmod 600 "$BACKUP_FILE"
for mime in "${MIME_TYPES[@]}"; do
  if ! awk -F '\t' -v value="$mime" '$1 == value { found=1 } END { exit !found }' "$BACKUP_FILE"; then
    previous="$(xdg-mime query default "$mime" 2>/dev/null || true)"
    printf '%s\t%s\n' "$mime" "$previous" >> "$BACKUP_FILE"
  fi
  xdg-mime default office-web-launcher.desktop "$mime"
done

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$APPLICATIONS_DIR" >/dev/null 2>&1 || true
fi

echo "Office Web Launcher is installed for this Linux user."
echo "Double-click a supported Office file; the first open will ask for Microsoft sign-in."
