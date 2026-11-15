# stable のノートは日本語と英語を <!-- lang: ja --> / <!-- lang: en --> の印で区切る（アプリが画面の言語の方を出す。Updates/ReleaseNotes.cs）。
[CmdletBinding(DefaultParameterSetName = 'Stable')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Stable')] [string] $Version,
    [Parameter(Mandatory, ParameterSetName = 'Nightly')] [string] $To,
    [Parameter(ParameterSetName = 'Nightly')] [string] $From = '',
    [Parameter(Mandatory)] [string] $Out,
    [string] $Root = (Join-Path $PSScriptRoot '..\..')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path $Root).Path
$out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
$utf8 = New-Object System.Text.UTF8Encoding $false

if ($PSCmdlet.ParameterSetName -eq 'Stable') {
    $ja = Join-Path $root "CHANGELOG\v${Version}_CHANGELOGS.md"
    $en = Join-Path $root "CHANGELOG\v${Version}_CHANGELOGS.en.md"
    foreach ($file in $ja, $en) {
        if (-not (Test-Path -LiteralPath $file)) { throw "no $file" }
    }
    $text = "<!-- lang: ja -->`n" + [IO.File]::ReadAllText($ja, $utf8).Trim() + "`n`n" +
            "<!-- lang: en -->`n" + [IO.File]::ReadAllText($en, $utf8).Trim() + "`n"
} else {
    $lines = @()
    if ($From -and $From -notmatch '^0+$') {
        $lines = @(& git -C $root log --no-merges --max-count=50 --format='- %s (%h)' "$From..$To" 2>$null)
        if ($LASTEXITCODE -ne 0) { $lines = @() }
    }
    if ($lines.Count -eq 0) { $lines = @(& git -C $root log -1 --format='- %s (%h)' $To) }
    if ($LASTEXITCODE -ne 0) { throw "git log failed for $To" }
    $text = ($lines -join "`n") + "`n"
}

New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
[IO.File]::WriteAllText($out, $text, $utf8)
$out
