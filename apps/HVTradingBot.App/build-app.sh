#!/usr/bin/env bash
# Builds HVTradingBot.app (Mac Catalyst, Apple Silicon + Intel), ad-hoc signs it and installs it in /Applications.
#
#   apps/HVTradingBot.App/build-app.sh             build and install
#   apps/HVTradingBot.App/build-app.sh --no-install build only (prints where the .app is)
#
# Needs macOS with Xcode and the .NET MAUI workload (dotnet workload install maui). The app is not signed by Apple;
# a locally built copy opens normally, a copy downloaded from elsewhere needs right-click -> Open the first time.
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_NAME="HVTradingBot.app"
DEST="/Applications/${APP_NAME}"
FRAMEWORK="net10.0-maccatalyst"
OUT_DIR="${PROJECT_DIR}/bin/Release/${FRAMEWORK}"

info() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31mError:\033[0m %s\n' "$*" >&2; exit 1; }

install=true
case "${1:-}" in
  "") ;;
  --no-install) install=false ;;
  *) fail "Unknown option: $1 (use --no-install to skip copying to /Applications)" ;;
esac

[ "$(uname -s)" = "Darwin" ] || fail "The Mac app can only be built on macOS."
command -v dotnet >/dev/null 2>&1 || fail ".NET SDK not found. Install .NET 10: brew install --cask dotnet-sdk"
xcode-select -p >/dev/null 2>&1 || fail "Xcode is required: install it from the App Store, then run: sudo xcode-select -s /Applications/Xcode.app"
dotnet workload list 2>/dev/null | grep -qE '^maui|^maui-maccatalyst' || fail "The MAUI workload is missing. Install it: sudo dotnet workload install maui"

info "Publishing ${APP_NAME} (Release, ${FRAMEWORK})"
rm -rf "$OUT_DIR"
# ValidateXcodeVersion=false: the MAUI SDK pins an exact Xcode version; a newer minor Xcode builds fine.
dotnet publish "${PROJECT_DIR}/HVTradingBot.App.csproj" -f "$FRAMEWORK" -c Release -p:CreatePackage=false -p:ValidateXcodeVersion=false -nologo

# The universal bundle is the shallowest HVTradingBot.app under the output folder (per-architecture copies sit deeper).
app="$(find "$OUT_DIR" -type d -name "$APP_NAME" -prune | awk '{ print length($0) "\t" $0 }' | sort -n | head -n 1 | cut -f 2-)"
[ -n "$app" ] && [ -d "$app" ] || fail "No ${APP_NAME} found under ${OUT_DIR}."

info "Signing ${app} (ad-hoc, App Sandbox off)"
codesign --force --deep --sign - --entitlements "${PROJECT_DIR}/Platforms/MacCatalyst/Entitlements.plist" "$app"
codesign --verify --deep --strict "$app"

if ! $install; then
  info "Built: ${app}"
  exit 0
fi

if pgrep -x HVTradingBot >/dev/null 2>&1; then
  info "Quitting the running app (the Docker stack keeps running)"
  osascript -e 'quit app "HVTradingBot"' >/dev/null 2>&1 || true
  sleep 2
fi

info "Installing to ${DEST}"
rm -rf "$DEST"
ditto "$app" "$DEST"
info "Done. Open HVTradingBot from Launchpad or /Applications."
