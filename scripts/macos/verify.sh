#!/usr/bin/env bash
set -euo pipefail

dmg=${1:?Pass a DMG}
root=$(mktemp -d "${RUNNER_TEMP:-/tmp}/focusbloom-mac.XXXXXX")
mount="$root/Install Volume"
mkdir -p "$mount" "$root/Test Applications"
hdiutil attach "$dmg" -readonly -nobrowse -mountpoint "$mount" >/dev/null
trap 'hdiutil detach "$mount" >/dev/null' EXIT
test "$(readlink "$mount/Applications")" = /Applications
app="$root/Test Applications/FocusBloom.app"
ditto "$mount/FocusBloom.app" "$app"
codesign --verify --deep --strict "$app"
binary="$app/Contents/MacOS/FocusBloom"
python3 - "$binary" "$root/Test Data" <<'PY'
import subprocess
import sys

binary, profile = sys.argv[1:]
result = subprocess.run([binary, '--data-dir', profile, '--smoke-test'], capture_output=True, text=True, timeout=100)
print(result.stdout)
print(result.stderr, file=sys.stderr)
if result.returncode != 0 or 'FOCUSBLOOM_SMOKE_OK' not in result.stdout:
    raise SystemExit('Packaged application failed to open or complete its smoke checks')
PY
