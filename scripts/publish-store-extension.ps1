$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$source = Join-Path $repo 'extensions/browser'
$output = Join-Path $repo ('artifacts/extension-store-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$files = @('background.js', 'blocked.html', 'cloud.mjs', 'focus.mjs', 'history.mjs',
    'language.mjs', 'page.css', 'page.js', 'popup.html', 'rules.mjs', 'sync.mjs', 'theme.mjs')

foreach ($platform in @('chromium', 'firefox')) {
    $folder = Join-Path $output $platform
    $icons = Join-Path $folder 'icons'
    New-Item -ItemType Directory -Path $icons | Out-Null
    foreach ($name in $files) {
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination $folder
    }
    foreach ($size in @(16, 48, 128)) {
        Copy-Item -LiteralPath (Join-Path $source "icons/$size.png") -Destination $icons
    }

    $manifest = Get-Content -LiteralPath (Join-Path $source 'manifest.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($platform -eq 'firefox') {
        $manifest.Remove('minimum_chrome_version')
        $manifest.background = @{ scripts = @('background.js'); type = 'module' }
        $manifest.browser_specific_settings = @{ gecko = @{
            id = 'focusbloom@amy22707.github.io'
            strict_min_version = '140.0'
            data_collection_permissions = @{ required = @('none') }
        }}
    }
    # Only the interstitial needs to be accessible from other sites.
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $folder 'manifest.json') -Encoding utf8
    $zip = Join-Path $output "FocusBloom-$platform.zip"
    Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $zip
    Write-Output "StoreArchive=$zip"
}
