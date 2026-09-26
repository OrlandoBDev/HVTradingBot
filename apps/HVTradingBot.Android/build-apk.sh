#!/usr/bin/env bash
# Builds the Android app (HVTradingBot.apk) from this checkout.
#
# Needs: .NET 10 SDK, Node.js 22, JDK 17+, and the Android SDK with platform 36:
#   dotnet workload install android
#   dotnet build apps/HVTradingBot.Android -t:InstallAndroidDependencies -p:AndroidSdkDirectory="$HOME/Android/Sdk" -p:AcceptAndroidSDKLicenses=True
#
# Signing: set ANDROID_KEYSTORE (path), ANDROID_KEYSTORE_PASSWORD and optionally ANDROID_KEY_ALIAS (default
# "hvtradingbot") to sign with your own key. Without them the APK is signed with the local debug key, and Android only
# installs an update over an app signed with the same key (see docs/ANDROID_APP.md).
set -euo pipefail

repo="$(cd "$(dirname "$0")/../.." && pwd)"
out="$repo/apps/HVTradingBot.Android/bin/apk"

echo "==> Building the dashboard"
(cd "$repo/web/HVTradingBot.Web" && npm ci && npm run build)

signing=()
if [[ -n "${ANDROID_KEYSTORE:-}" ]]; then
  echo "==> Signing with $ANDROID_KEYSTORE"
  signing=(
    -p:AndroidKeyStore=true
    "-p:AndroidSigningKeyStore=$ANDROID_KEYSTORE"
    "-p:AndroidSigningKeyAlias=${ANDROID_KEY_ALIAS:-hvtradingbot}"
    "-p:AndroidSigningKeyPass=env:ANDROID_KEYSTORE_PASSWORD"
    "-p:AndroidSigningStorePass=env:ANDROID_KEYSTORE_PASSWORD"
  )
else
  echo "==> No ANDROID_KEYSTORE set: signing with the debug key"
fi

echo "==> Building the app"
dotnet publish "$repo/apps/HVTradingBot.Android/HVTradingBot.Android.csproj" -c Release -o "$out" ${signing[@]+"${signing[@]}"} "$@"

apk="$(ls "$out"/*-Signed.apk)"
cp "$apk" "$out/HVTradingBot.apk"
echo "==> $out/HVTradingBot.apk"
