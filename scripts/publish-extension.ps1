$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$extensionSource = Join-Path $repoRoot 'extensions/browser'
$extensionOutput = Join-Path $repoRoot 'artifacts/browser-extension'
New-Item -ItemType Directory -Force $extensionOutput | Out-Null
Get-ChildItem -LiteralPath $extensionSource | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $extensionOutput -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/browser-extension.md') -Destination (Join-Path $extensionOutput '使用说明.md') -Force
Compress-Archive -Path (Join-Path $extensionOutput '*') -DestinationPath (Join-Path $repoRoot 'artifacts/FocusBloom-browser-extension.zip') -Force
Write-Host '独立浏览器扩展已发布到 artifacts/browser-extension，压缩包为 artifacts/FocusBloom-browser-extension.zip。'
