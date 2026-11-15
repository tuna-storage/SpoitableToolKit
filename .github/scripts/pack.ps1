# - packId は「SpoitableToolKit.App」。データの置き場（%LOCALAPPDATA%\SpoitableToolKit）と同じ名前にしない —
#   Velopack はインストール先（%LOCALAPPDATA%\<packId>）を、入れ直すときもアンインストールするときも中身ごと消す。
#   一度出したら変えない（変えると別のアプリとして扱われ、入っている物に更新が届かない）。
# - 差分の nupkg は作らない（--delta None）。各 Release の releases.<channel>.json が自分の物だけを指すので、
#   stable の確かめが「最新の Release」だけを見て済む（Updates/VelopackUpdateSource.cs）。
# - Visual C++ の runtime は CefSharp が要る。入っていない PC では Setup が入れる（--framework）。
# - nupkg と json の名前は変えない（アプリはその名前で取りに行く）。名前を変えるのは Setup.exe だけ。
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [ValidateSet('stable', 'nightly')] [string] $Channel,
    [Parameter(Mandatory)] [string] $Notes,
    [string] $Out = 'artifacts\packages',
    [string] $Root = (Join-Path $PSScriptRoot '..\..')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path $Root).Path
$out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
$notes = (Resolve-Path $Notes).Path
$publish = Join-Path $root 'artifacts\publish'

foreach ($dir in $publish, $out) {
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
}

# 同梱モジュールは Host の参照ではないので、Host の publish の restore には入らない。解ごと先に restore する（clone したばかりでも通るように）。
& dotnet restore (Join-Path $root 'SpoitableToolKit.slnx') -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed ($LASTEXITCODE)" }

& dotnet publish (Join-Path $root 'src\SpoitableToolKit.Host\SpoitableToolKit.Host.csproj') `
    -c Release -r win-x64 --self-contained true -o $publish "-p:AppVersion=$Version" -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

foreach ($module in 'hrs', 'videourl') {
    if (-not (Test-Path -LiteralPath (Join-Path $publish "modules\$module\module.json"))) { throw "modules\$module is not in the publish" }
}
$sdk = @(Get-ChildItem -LiteralPath (Join-Path $publish 'sdk') -Filter '*.nupkg')
if ($sdk.Count -ne 1) { throw "sdk\ should hold one package, not $($sdk.Count)" }

Push-Location $root
try {
    & dotnet tool run vpk pack `
        --packId SpoitableToolKit.App `
        --packTitle SpoitableToolKit `
        --packAuthors SpoitableToolKit `
        --packVersion $Version `
        --packDir $publish `
        --mainExe SpoitableToolKit.Host.exe `
        --icon (Join-Path $root 'src\SpoitableToolKit.Host\Assets\app-icon.ico') `
        --runtime win-x64 `
        --channel $Channel `
        --releaseNotes $notes `
        --framework vcredist143-x64 `
        --delta None `
        --noPortable `
        --outputDir $out
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed ($LASTEXITCODE)" }
} finally {
    Pop-Location
}

$setup = @(Get-ChildItem -LiteralPath $out -Filter '*-Setup.exe')
if ($setup.Count -ne 1) { throw "expected one Setup.exe, found $($setup.Count)" }
$name = if ($Channel -eq 'stable') { 'SpoitableToolKit-Setup.exe' } else { 'SpoitableToolKit-nightly-Setup.exe' }
Rename-Item -LiteralPath $setup[0].FullName -NewName $name

# assets.<channel>.json は vpk の upload / download が読む一覧で、アプリは読まない。Setup.exe の前の名前を指したままになるので配らない。
Get-ChildItem -LiteralPath $out -Filter 'assets.*.json' | Remove-Item

Get-ChildItem -LiteralPath $out | Select-Object Name, Length
