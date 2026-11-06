param([string]$Version = '1.0.1', [switch]$Installer)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $repo '.tools/dotnet/dotnet.exe'
$env:DOTNET_CLI_HOME = Join-Path $repo '.tools/cli-home'
$env:NUGET_PACKAGES = Join-Path $repo '.tools/nuget'
$stage = Join-Path $repo ('artifacts/offline-release-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$app = Join-Path $stage 'FocusBloom'
$zip = Join-Path $stage 'FocusBloom-Windows-x64-Offline.zip'

& $dotnet publish (Join-Path $repo 'src/FocusBloom.Desktop') -c Release -r win-x64 --self-contained true -p:OfflineRelease=true "-p:Version=$Version" -o $app
if ($LASTEXITCODE -ne 0) { throw '离线版构建失败。' }

Copy-Item -LiteralPath (Join-Path $repo 'docs/offline-release.md') -Destination (Join-Path $app '使用说明.md')
Copy-Item -LiteralPath (Join-Path $repo 'docs/quote-sources.md') -Destination (Join-Path $app '默认句子出处.md')
$licenses = Join-Path $app 'ThirdPartyLicenses'
New-Item -ItemType Directory -Path $licenses | Out-Null
$deps = Get-Content -LiteralPath (Join-Path $app 'FocusBloom.deps.json') -Raw | ConvertFrom-Json -AsHashtable
$index = @('# 随包依赖与许可', '')
foreach ($dependency in $deps.libraries.Keys | Sort-Object) {
    if ($deps.libraries[$dependency].type -eq 'project') { continue }
    $key = $dependency -replace '^runtimepack\.', ''
    $packageName, $packageVersion = $key.Split('/', 2)
    $package = Join-Path (Join-Path $env:NUGET_PACKAGES $packageName.ToLowerInvariant()) $packageVersion
    $spec = Get-ChildItem -LiteralPath $package -Filter '*.nuspec' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $spec) { continue } # Individual WebView2 assemblies use the SDK package notice.
    [xml]$xml = Get-Content -LiteralPath $spec.FullName -Raw
    $metadata = $xml.package.metadata
    $terms = if ($metadata.license) { [string]$metadata.license.InnerText } else { [string]$metadata.licenseUrl }
    $index += "- $packageName $packageVersion — $terms"
    $notices = Get-ChildItem -LiteralPath $package -File | Where-Object Name -Match '^(license|notice|third-party-notices)(\.|$)'
    if ($notices) {
        $target = Join-Path $licenses $packageName
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $target }
    }
}
$index | Set-Content -LiteralPath (Join-Path $licenses '依赖清单.md') -Encoding utf8
foreach ($name in @('cloud-config.json','cloudbase.local.json','account.session','extension')) {
    if (Test-Path -LiteralPath (Join-Path $app $name)) { throw "离线包包含意外内容：$name" }
}
Compress-Archive -LiteralPath $app -DestinationPath $zip -CompressionLevel Fastest
Write-Output "OfflineArchive=$zip"
if ($Installer) {
    $wix = Join-Path $repo '.tools/wix/wix.exe'
    if (!(Test-Path -LiteralPath $wix)) { throw '请先将 WiX 工具安装到 .tools/wix。' }
    $msi = Join-Path $stage 'FocusBloom-Windows-x64.msi'
    & $wix build (Join-Path $PSScriptRoot 'FocusBloom.wxs') -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext -culture zh-CN -arch x64 -d "Version=$Version" -d "Payload=$app" -o $msi
    if ($LASTEXITCODE -ne 0) { throw 'MSI 打包失败。' }
    Write-Output "OfflineInstaller=$msi"
}
