$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$toolRoot = Join-Path $repoRoot '.tools'
$sdkRoot = Join-Path $toolRoot 'dotnet'
New-Item -ItemType Directory -Force $toolRoot | Out-Null
$installer = Join-Path $toolRoot 'dotnet-install.ps1'
Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
& $installer -Channel 10.0 -InstallDir $sdkRoot -NoPath
& (Join-Path $sdkRoot 'dotnet.exe') --version
if ($LASTEXITCODE -ne 0) { throw 'SDK 安装失败。' }
Write-Host "SDK 已安装到 $sdkRoot"
