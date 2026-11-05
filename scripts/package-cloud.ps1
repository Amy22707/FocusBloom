$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$output = Join-Path $repoRoot 'artifacts/cloud'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'cloud/postgresql/setup.sql') -Destination (Join-Path $output 'focusbloom-postgresql.sql') -Force
Write-Host 'PostgreSQL部署脚本已生成：artifacts/cloud/focusbloom-postgresql.sql'
