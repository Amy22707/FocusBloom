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
python3 - "$binary" "$root/Test Data" "$app" <<'PY'
import subprocess
import sys
from pathlib import Path

binary, profile, app = sys.argv[1:]
process = subprocess.Popen([binary, '--data-dir', profile, '--smoke-test'], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
try:
    output, errors = process.communicate(timeout=90)
except subprocess.TimeoutExpired:
    sample = subprocess.run(['/usr/bin/sample', str(process.pid), '2'], capture_output=True, text=True, timeout=15)
    print(sample.stdout, flush=True)
    print(sample.stderr, file=sys.stderr, flush=True)
    process.kill()
    output, errors = process.communicate()
    print(output, flush=True)
    print(errors, file=sys.stderr, flush=True)
    raise SystemExit('Packaged application did not finish its startup checks')
print(output)
print(errors, file=sys.stderr)
if process.returncode != 0 or 'FOCUSBLOOM_SMOKE_OK' not in output:
    raise SystemExit('Packaged application failed to open or complete its smoke checks')

finder_profile = profile + ' Finder Launch'
subprocess.run(['/usr/bin/open', '-W', '-n', app, '--args', '--data-dir', finder_profile, '--smoke-test'],
               check=True, timeout=100)
if not Path(finder_profile, 'smoke-success.txt').is_file():
    raise SystemExit('The copied application did not complete its Finder launch checks')
print('Copied application launched through macOS Launch Services')
PY
