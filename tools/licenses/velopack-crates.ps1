<#
  Velopack の Update.exe・Setup.exe に入る Rust の crate のライセンスを、THIRD-PARTY-NOTICES.md の節の形で書き出す。
  使い方は docs/third-party-notices.md の「Velopack の Rust の crate」。

  落としてきたもの（Velopack のソース・crate）は信頼できないデータとして扱う:
  - 置き場は -Work の空のフォルダ。cargo は -Work\cwd から --manifest-path で呼ぶ（cargo の設定は今いるフォルダから探されるので、
    Velopack の repo の .cargo/config.toml を読ませない）。CARGO_HOME も -Work の中にして、手元の ~/.cargo を汚さない
  - 走らせるのは git の fetch・checkout と cargo tree だけ。どちらも crate のコード（build script・proc macro）を走らせない
#>
param(
    [Parameter(Mandatory)] [string] $Commit,   # Velopack の nuspec の <repository commit="…">
    [Parameter(Mandatory)] [string] $Work,     # 空のフォルダ（無ければ作る）
    [Parameter(Mandatory)] [string] $Out,
    [string] $Target = 'x86_64-win7-windows-msvc'   # Velopack の Windows（x64）の build と同じ
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)

$src = Join-Path $Work 'velopack'
$cwd = Join-Path $Work 'cwd'
New-Item -ItemType Directory -Force $Work, $cwd | Out-Null

if (-not (Test-Path (Join-Path $src '.git'))) {
    New-Item -ItemType Directory -Force $src | Out-Null
    git -C $src init -q
    git -C $src remote add origin https://github.com/velopack/velopack.git
}
git -C $src fetch -q --depth 1 origin $Commit
git -C $src -c core.hooksPath=NUL checkout -q --force FETCH_HEAD
if ((git -C $src rev-parse HEAD) -ne $Commit) { throw "checkout of $Commit failed" }

# Velopack の Windows の build（.github/workflows/build-rust.yml）と同じ: workspace を python 抜きで、features windows。
# normal の依存だけ（build・dev を除く）、proc macro も除く（build のときに走るだけで、exe には入らない）。
$env:CARGO_HOME = Join-Path $Work 'cargo-home'
Push-Location $cwd
try {
    $tree = & cargo tree --locked --manifest-path (Join-Path $src 'Cargo.toml') --workspace --exclude velopack_python --features windows `
        --target $Target -e normal,no-proc-macro --no-dedupe --prefix depth --format '{p}|{l}'
    if ($LASTEXITCODE -ne 0) { throw "cargo tree failed ($LASTEXITCODE)" }
}
finally {
    Pop-Location
}

# velopack_bins（Update.exe・Setup.exe を作る package）の下だけ。workspace の中の crate（velopack・velopack_l18n）は Velopack の MIT の節が持つ。
$crates = [ordered]@{}
$inside = $false
foreach ($line in $tree) {
    if ($line -notmatch '^(\d+)(.+)\|(.*)$') { continue }
    $depth = [int]$Matches[1]; $package = $Matches[2]; $expression = $Matches[3]
    if ($depth -eq 0) { $inside = $package.StartsWith('velopack_bins '); continue }
    if (-not $inside -or $package -match '\(.+\)$') { continue }
    if ($package -notmatch '^(\S+) v(\S+)$') { throw "unexpected line: $line" }
    $crates["$($Matches[1]) $($Matches[2])"] = [pscustomobject]@{ Name = $Matches[1]; Version = $Matches[2]; Expression = $expression }
}

$sources = (Get-ChildItem (Join-Path $env:CARGO_HOME 'registry\src') -Directory | Select-Object -First 1).FullName

# 選べるもの（OR）は、この順で先にあるものを使う。全文を一覧の末尾（MIT・Apache 2.0）か、この節に置く。
$preference = @('MIT', 'Apache-2.0', 'Unicode-3.0', 'BSD-3-Clause', 'ISC', 'Zlib', 'CDLA-Permissive-2.0', '0BSD', 'Unlicense')
$titles = @{
    'MIT' = 'MIT License (the text is under "MIT License" in License texts)'
    'Apache-2.0' = 'Apache License 2.0 (the text is under "Apache License 2.0" in License texts)'
    'Unicode-3.0' = 'Unicode License v3'
    'BSD-3-Clause' = 'BSD 3-Clause License'
    'ISC' = 'ISC License'
    'Zlib' = 'zlib License'
    'CDLA-Permissive-2.0' = 'Community Data License Agreement - Permissive - Version 2.0'
}

# この crate の LICENSE が指している、ほかの場所のライセンスの文。
$extraFiles = @{ 'ring' = @('src/polyfill/once_cell/LICENSE-MIT') }

function LicenseFiles($dir) {
    @(Get-ChildItem $dir -File | Where-Object { $_.Name -match '^(LICEN[CS]E|COPYING|COPYRIGHT|NOTICE)' } | Sort-Object Name)
}

function ChosenFiles($files, $chosen) {
    $notGpl = @($files | Where-Object { $_.Name -notmatch 'GPL' })
    $byName = switch ($chosen) {
        'MIT' { @($notGpl | Where-Object { $_.Name -match 'MIT' }) }
        'Apache-2.0' { @($notGpl | Where-Object { $_.Name -match 'APACHE|NOTICE' }) }
        default { @($notGpl | Where-Object { $_.Name -notmatch 'MIT|APACHE' }) }
    }
    if ($byName.Count) { $byName } else { $notGpl }
}

function CopyrightLines($files) {
    $lines = foreach ($file in $files) {
        foreach ($raw in [IO.File]::ReadAllLines($file.FullName, [Text.Encoding]::UTF8)) {
            $line = ($raw -replace '^\s*(//+|#+|\*+)?\s*', '').Trim()
            if ($line -notmatch ('^(copyright|\(c\)|' + [char]0xA9 + ')')) { continue }
            if ($line -match '^\(c\) You must retain|^copyright license|^COPYRIGHT AND PERMISSION NOTICE|\[yyyy\]|\{yyyy\}|<year>|\[year\]|copyright notice') { continue }
            $line
        }
    }
    @($lines | Select-Object -Unique)
}

function Indent([string] $text) {
    (($text -replace "`r`n", "`n").TrimEnd("`n") -split "`n" | ForEach-Object { if ($_.Trim().Length) { '    ' + $_.TrimEnd() } else { '' } }) -join "`n"
}

$entries = foreach ($crate in $crates.Values) {
    $dir = Join-Path $sources "$($crate.Name)-$($crate.Version)"
    if (-not (Test-Path $dir)) { throw "no source for $($crate.Name) $($crate.Version)" }

    $normalized = $crate.Expression -replace '/', ' OR '
    $choices = @($normalized -split ' OR ' | ForEach-Object { $_.Trim() })
    $chosen = if ($normalized -match ' AND ') { $normalized } else { $preference | Where-Object { $choices -contains $_ } | Select-Object -First 1 }
    if (-not $chosen) { throw "no known license for $($crate.Name): $($crate.Expression)" }

    $files = LicenseFiles $dir
    $chosenFiles = if ($normalized -match ' AND ') { @($files | Where-Object { $_.Name -notmatch 'GPL' }) } else { ChosenFiles $files $chosen }
    foreach ($extra in @($extraFiles[$crate.Name])) { if ($extra -and (Test-Path (Join-Path $dir $extra))) { $chosenFiles += Get-Item (Join-Path $dir $extra) } }

    $toml = [IO.File]::ReadAllText((Join-Path $dir 'Cargo.toml'), [Text.Encoding]::UTF8)
    $authors = @()
    $m = [regex]::Match($toml, '(?ms)^authors\s*=\s*\[(.*?)\]')
    if ($m.Success) { $authors = @([regex]::Matches($m.Groups[1].Value, '"([^"<]*?)\s*(<[^"]*>)?"') | ForEach-Object { $_.Groups[1].Value.Trim() } | Where-Object { $_ }) }

    [pscustomobject]@{
        Name = $crate.Name; Version = $crate.Version; Expression = $crate.Expression; Chosen = $chosen
        Files = $chosenFiles; Dir = $dir; Copyright = @(CopyrightLines $chosenFiles); Authors = $authors
    }
}

function Attribution($entry) {
    if ($entry.Copyright.Count) { return ($entry.Copyright -join '; ') }
    if ($entry.Authors.Count) { return 'authors: ' + ($entry.Authors -join ', ') }
    'the crate gives no copyright line or author'
}

function Offered($entry) {
    if ($entry.Expression -eq $entry.Chosen) { '' } else { " (offered as $($entry.Expression))" }
}

$md = New-Object Text.StringBuilder
$count = @($entries).Count
[void]$md.AppendLine("- Crates: $count, the normal dependencies of the velopack_bins package (which builds Update.exe and Setup.exe) in Cargo.lock, as Velopack builds it for Windows (features `"windows`", target $Target). Build-only, dev-only and procedural-macro crates are left out. The source of each is at https://crates.io/crates/<name>/<version>.")
[void]$md.AppendLine('- License: each crate''s own. Where a crate offers a choice (for example "MIT OR Apache-2.0"), the app uses the one it is listed under below.')
[void]$md.AppendLine()

$groups = $entries | Group-Object Chosen | Sort-Object { $i = [array]::IndexOf($preference, $_.Name); if ($i -lt 0) { 99 } else { $i } }, Name
foreach ($group in $groups) {
    $title = if ($titles.ContainsKey($group.Name)) { $titles[$group.Name] } else { $group.Name }
    [void]$md.AppendLine("#### $title ($($group.Count) $(if ($group.Count -eq 1) { 'crate' } else { 'crates' }))")
    [void]$md.AppendLine()

    $sharedText = $group.Name -in @('MIT', 'Apache-2.0')
    if ($group.Name -eq 'Unicode-3.0') {
        $texts = @($group.Group | ForEach-Object { [IO.File]::ReadAllText($_.Files[0].FullName, [Text.Encoding]::UTF8) -replace "`r`n", "`n" } | Select-Object -Unique)
        if ($texts.Count -eq 1) { $sharedText = $true }
    }

    foreach ($entry in ($group.Group | Sort-Object Name, Version)) {
        [void]$md.AppendLine("- $($entry.Name) $($entry.Version)$(Offered $entry): $(Attribution $entry)")
        if (-not $sharedText) {
            foreach ($file in $entry.Files) {
                [void]$md.AppendLine()
                [void]$md.AppendLine((Indent ("[" + $file.FullName.Substring($entry.Dir.Length + 1).Replace('\', '/') + "]`n" + [IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8))))
            }
            [void]$md.AppendLine()
        }
    }

    if ($group.Name -eq 'Unicode-3.0' -and $sharedText) {
        [void]$md.AppendLine()
        [void]$md.AppendLine((Indent ([IO.File]::ReadAllText($group.Group[0].Files[0].FullName, [Text.Encoding]::UTF8))))
    }
    [void]$md.AppendLine()
}

[IO.File]::WriteAllText($Out, ($md.ToString() -replace "`r`n", "`n").TrimEnd("`n") + "`n", $utf8)
"crates: $count"
$groups | ForEach-Object { "  {0}: {1}" -f $_.Name, $_.Count }
