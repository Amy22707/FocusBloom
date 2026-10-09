param(
    [ValidateSet('build', 'test', 'run', 'publish')]
    [string]$Task = 'run',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$localDotnet = Join-Path $repoRoot '.tools/dotnet/dotnet.exe'
if (Test-Path $localDotnet) {
    $dotnet = $localDotnet
    $env:DOTNET_ROOT = Split-Path $localDotnet -Parent
} else {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}
$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.tools/cli-home'
$env:NUGET_PACKAGES = Join-Path $repoRoot '.tools/nuget'
Push-Location $repoRoot
try {
    switch ($Task) {
        'build' { & $dotnet build FocusBloom.sln -c $Configuration }
        'test' { & $dotnet test FocusBloom.sln -c $Configuration }
        'run' { & $dotnet run --project src/FocusBloom.Desktop -c $Configuration }
        'publish' {
            & $dotnet publish src/FocusBloom.Desktop -c Release -r win-x64 --self-contained true -o artifacts/windows-x64
            if ($LASTEXITCODE -ne 0) { throw '桌面程序发布失败。' }
            $extensionOutput = Join-Path $repoRoot 'artifacts/windows-x64/extension'
            New-Item -ItemType Directory -Force $extensionOutput | Out-Null
            Get-ChildItem -LiteralPath (Join-Path $repoRoot 'extensions/browser') | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination $extensionOutput -Recurse -Force
            }
            & (Join-Path $PSScriptRoot 'publish-extension.ps1')
        }
    }
    if ($LASTEXITCODE -ne 0) { throw "$Task 失败，退出码：$LASTEXITCODE" }
} finally {
    Pop-Location
}
