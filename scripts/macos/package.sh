#!/usr/bin/env bash
set -euo pipefail

arch=${1:?Pass arm64 or x64}
case "$arch" in arm64|x64) ;; *) exit 2 ;; esac
output=${2:?Pass an output directory}
repo=$(cd "$(dirname "$0")/../.." && pwd)
mkdir -p "$output"
output=$(cd "$output" && pwd)
version=$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$repo/Directory.Build.props")
staging="$output/staging-$arch"
app="$staging/FocusBloom.app"
contents="$app/Contents"
mkdir -p "$contents/MacOS" "$contents/Resources"

dotnet publish "$repo/src/FocusBloom.Mac/FocusBloom.Mac.csproj" -c Release -r "osx-$arch" \
  --self-contained true -o "$contents/MacOS"
mv "$contents/MacOS/FocusBloom.Mac" "$contents/MacOS/FocusBloom"
swiftc -emit-library "$repo/scripts/macos/notify.swift" -o "$contents/MacOS/libFocusBloomNotifications.dylib"
cat > "$contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>FocusBloom</string>
  <key>CFBundleDisplayName</key><string>FocusBloom</string>
  <key>CFBundleIdentifier</key><string>io.github.amy22707.focusbloom</string>
  <key>CFBundleExecutable</key><string>FocusBloom</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>CFBundleIconFile</key><string>FocusBloom.icns</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSAppTransportSecurity</key><dict><key>NSAllowsLocalNetworking</key><true/></dict>
</dict></plist>
EOF
curl --fail --location "https://github.com/Amy22707/FocusBloom/releases/download/v$version/FocusBloom-chromium.zip" -o "$output/browser-assets.zip"
python3 - "$output/browser-assets.zip" "$output/FocusBloom.png" <<'PY'
import sys
import zipfile
from pathlib import Path

with zipfile.ZipFile(sys.argv[1]) as archive:
    names = {name.replace('\\', '/'): name for name in archive.namelist()}
    Path(sys.argv[2]).write_bytes(archive.read(names['icons/128.png']))
PY
cp "$output/FocusBloom.png" "$contents/Resources/FocusBloom.png"
swift "$repo/scripts/macos/icon.swift" "$output/FocusBloom.png" "$output/FocusBloom.iconset"
iconutil -c icns "$output/FocusBloom.iconset" -o "$contents/Resources/FocusBloom.icns"
plutil -lint "$contents/Info.plist"
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict "$app"
ln -s /Applications "$staging/Applications"
dmg="$output/FocusBloom-macOS-$arch.dmg"
hdiutil create -volname "FocusBloom" -srcfolder "$staging" -format UDZO -ov "$dmg"
hdiutil verify "$dmg"
printf '%s\n' "$dmg"
