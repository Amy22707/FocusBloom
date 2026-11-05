param([string]$ConfigurationFile)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $ConfigurationFile) { $ConfigurationFile = Join-Path $repoRoot 'cloudbase.local.json' }
if (-not (Test-Path -LiteralPath $ConfigurationFile)) { Write-Host '未提供本机云端配置，继续使用本机功能。'; return }
$config = Get-Content -LiteralPath $ConfigurationFile -Raw | ConvertFrom-Json
if (-not $config.environmentId -or -not $config.publishableKey -or $config.region -ne 'ap-shanghai') { throw '请填写上海环境ID和Publishable Key。' }
$payload = $config.publishableKey.Split('.')[1].Replace('-','+').Replace('_','/')
$payload = $payload.PadRight($payload.Length + (4 - $payload.Length % 4) % 4, '=')
$claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
if ($claims.role -ne 'anon' -or $claims.is_system_admin -eq $true -or $claims.aud -ne $config.environmentId) { throw '客户端只接受此环境的Publishable Key，不能使用管理员密钥。' }
Copy-Item -LiteralPath $ConfigurationFile -Destination (Join-Path $repoRoot 'extensions/browser/cloud-config.json') -Force
Write-Host '已配置本机桌面和浏览器扩展的开发环境。'
