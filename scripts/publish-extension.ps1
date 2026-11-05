$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'configure-cloud.ps1')
$extensionSource = Join-Path $repoRoot 'extensions/browser'
$extensionOutput = Join-Path $repoRoot 'artifacts/browser-extension'
New-Item -ItemType Directory -Force $extensionOutput | Out-Null
Get-ChildItem -LiteralPath $extensionSource | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $extensionOutput -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/browser-extension.md') -Destination (Join-Path $extensionOutput '使用说明.md') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'cloud/postgresql/setup.sql') -Destination (Join-Path $extensionOutput 'focusbloom-postgresql.sql') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'cloudbase.example.json') -Destination (Join-Path $extensionOutput 'cloudbase.example.json') -Force
$cloudGuide = [IO.File]::ReadAllText((Join-Path $repoRoot 'docs/cloudbase-setup.md')).Replace('(../cloudbase.example.json)', '(cloudbase.example.json)').Replace('(../cloud/postgresql/setup.sql)', '(focusbloom-postgresql.sql)')
[IO.File]::WriteAllText((Join-Path $extensionOutput 'cloudbase-setup.md'), $cloudGuide, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $repoRoot 'cloud/email/verification.html') -Destination (Join-Path $extensionOutput 'verification-email.html') -Force
$mailGuide = [IO.File]::ReadAllText((Join-Path $repoRoot 'docs/branded-email.md')).Replace('(../cloud/email/verification.html)', '(verification-email.html)')
[IO.File]::WriteAllText((Join-Path $extensionOutput 'branded-email.md'), $mailGuide, [Text.UTF8Encoding]::new($false))
Compress-Archive -Path (Join-Path $extensionOutput '*') -DestinationPath (Join-Path $repoRoot 'artifacts/FocusBloom-browser-extension.zip') -Force
Write-Host '独立浏览器扩展已发布到 artifacts/browser-extension，压缩包为 artifacts/FocusBloom-browser-extension.zip。'
