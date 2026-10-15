param([Parameter(Mandatory=$true)][string]$Filter, [ValidateSet('Debug','Release')][string]$Configuration = 'Release', [switch]$Worker, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repoRoot
if ($Worker) {
    $env:DOTNET_CLI_UI_LANGUAGE = 'en-US'
    $env:DOTNET_ROOT = Join-Path $repoRoot '.tools/dotnet'
    $env:DOTNET_CLI_HOME = Join-Path $repoRoot '.tools/cli-home'
    $env:NUGET_PACKAGES = Join-Path $repoRoot '.tools/nuget'
    & (Join-Path $env:DOTNET_ROOT 'dotnet.exe') test tests/FocusBloom.Tests/FocusBloom.Tests.csproj -c $Configuration --no-build --no-restore --filter "($Filter)&DesktopMode!=Interactive" --results-directory $OutputDirectory --logger 'trx;LogFileName=results.trx' *> (Join-Path $OutputDirectory 'output.log')
    exit $LASTEXITCODE
}
Add-Type -Path (Join-Path $PSScriptRoot 'test-desktop-native.cs')
$desktopName = 'FocusBloomTests-' + [Guid]::NewGuid().ToString('N')
$OutputDirectory = Join-Path $repoRoot ('artifacts/qa/isolated-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$inputBefore = [FocusBloomTestDesktop]::InputName()
$desktopHandle = [FocusBloomTestDesktop]::CreateDesktop($desktopName,[IntPtr]::Zero,[IntPtr]::Zero,0,0xC7,[IntPtr]::Zero)
if ($desktopHandle -eq [IntPtr]::Zero) { throw [ComponentModel.Win32Exception]::new() }
$process = [FocusBloomTestDesktop+ProcessInfo]::new()
try {
    $env:FOCUSBLOOM_TEST_DESKTOP = $desktopName
    $startup = [FocusBloomTestDesktop+Startup]::new()
    $startup.cb = [Runtime.InteropServices.Marshal]::SizeOf($startup)
    $startup.desktop = 'WinSta0\' + $desktopName
    $executable = (Get-Process -Id $PID).Path
    $command = [Text.StringBuilder]::new('"' + $executable + '" -NoLogo -NoProfile -File "' + $PSCommandPath + '" -Worker -Filter "' + $Filter + '" -Configuration ' + $Configuration + ' -OutputDirectory "' + $OutputDirectory + '"')
    if (-not [FocusBloomTestDesktop]::CreateProcess($executable,$command,[IntPtr]::Zero,[IntPtr]::Zero,$false,0x08000000,[IntPtr]::Zero,$repoRoot,[ref]$startup,[ref]$process)) { throw [ComponentModel.Win32Exception]::new() }
    [void][FocusBloomTestDesktop]::CloseHandle($process.thread)
    Write-Output "TestDesktop=$desktopName; InputDesktop=$inputBefore; Results=$OutputDirectory"
    while ([FocusBloomTestDesktop]::WaitForSingleObject($process.process,1000) -eq 258) { }
    $exitCode = 0
    [void][FocusBloomTestDesktop]::GetExitCodeProcess($process.process,[ref]$exitCode)
    Get-Content -LiteralPath (Join-Path $OutputDirectory 'output.log')
    $inputAfter = [FocusBloomTestDesktop]::InputName()
    Write-Output "InputDesktopAfter=$inputAfter; ExitCode=$exitCode"
    if ($inputBefore -ne $inputAfter) { throw '活动桌面发生变化，停止界面验证。' }
    if ($exitCode -ne 0) { throw "隔离桌面测试失败，退出码：$exitCode" }
} finally {
    if ($process.process -ne [IntPtr]::Zero) { [void][FocusBloomTestDesktop]::CloseHandle($process.process) }
    [void][FocusBloomTestDesktop]::CloseDesktop($desktopHandle)
    Remove-Item Env:FOCUSBLOOM_TEST_DESKTOP -ErrorAction SilentlyContinue
}
