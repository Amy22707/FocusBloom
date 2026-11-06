$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$archives = @(& (Join-Path $PSScriptRoot 'publish-store-extension.ps1'))
$archive = ($archives | Where-Object { $_ -like 'StoreArchive=*FocusBloom-chromium.zip' }) -replace '^StoreArchive=', ''
if (-not $archive) { throw '未能生成 Chromium 扩展包。' }
$extensionOutput = Join-Path $repoRoot 'artifacts/browser-extension'
New-Item -ItemType Directory -Force $extensionOutput | Out-Null
foreach ($name in @('cloud-config.json','cloud.mjs','sync.mjs','focusbloom-postgresql.sql','cloudbase.example.json','cloudbase-setup.md','verification-email.html','branded-email.md')) {
    $oldFile = Join-Path $extensionOutput $name
    if (Test-Path -LiteralPath $oldFile -PathType Leaf) { Remove-Item -LiteralPath $oldFile }
}
Expand-Archive -LiteralPath $archive -DestinationPath $extensionOutput -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/browser-extension.md') -Destination (Join-Path $extensionOutput '使用说明.md') -Force
Copy-Item -LiteralPath $archive -Destination (Join-Path $repoRoot 'artifacts/FocusBloom-browser-extension.zip') -Force
$archives | Write-Output
Write-Host '离线浏览器扩展已发布到 artifacts/browser-extension，压缩包为 artifacts/FocusBloom-browser-extension.zip。'
