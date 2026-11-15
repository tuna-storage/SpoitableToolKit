# アプリのアイコン（src/SpoitableToolKit.Host/Assets/app-icon.ico）を、同じ所の app-icon.svg から作り直す。exe・窓・Setup.exe が使う。
# 描くのは Edge のヘッドレス。画面と同じ Chromium で描くので、サイドバーのロゴと同じ縁の滑らかさになる。ほかの道具は入れない。
# 16〜128 は 32bit の BMP、256 だけ PNG で詰める（BMP しか読めない所があり、256 を BMP にすると 256KB になる）。
#
#   powershell -File tools\app-icon\make-icon.ps1 [-Preview <見本の PNG>] [-Edge <msedge.exe か chrome.exe>]
#
# 作り直したら build し直す（AppIconTests が exe の中の絵と .ico を突き合わせる）。
param(
    [string] $Preview,
    [string] $Edge
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assets = (Resolve-Path (Join-Path $PSScriptRoot '..\..\src\SpoitableToolKit.Host\Assets')).Path
$svgPath = Join-Path $assets 'app-icon.svg'
$icoPath = Join-Path $assets 'app-icon.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

if (-not $Edge) {
    $Edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $Edge) { throw 'msedge.exe が見つからない。-Edge で msedge.exe か chrome.exe の場所を渡すこと' }
}

function Read-Bgra([System.Drawing.Bitmap] $bitmap) {
    $rect = New-Object System.Drawing.Rectangle 0, 0, $bitmap.Width, $bitmap.Height
    $bits = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $bytes = New-Object byte[] ($bitmap.Width * $bitmap.Height * 4)
        for ($y = 0; $y -lt $bitmap.Height; $y++) {
            [Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($bits.Scan0, $y * $bits.Stride), $bytes, $y * $bitmap.Width * 4, $bitmap.Width * 4)
        }
        return , $bytes
    } finally {
        $bitmap.UnlockBits($bits)
    }
}

# 高さは 2 倍に書く（色の上に AND の mask が続く決まり）。行は下から。mask は透明な所を 1 にする（alpha を見ない古い描き方のため）。
function ConvertTo-IconBmp([System.Drawing.Bitmap] $bitmap) {
    $n = $bitmap.Width
    $pixels = Read-Bgra $bitmap
    $maskStride = [int][Math]::Ceiling($n / 32.0) * 4
    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter $stream
    $writer.Write([int]40); $writer.Write([int]$n); $writer.Write([int]($n * 2)); $writer.Write([int16]1); $writer.Write([int16]32)
    $writer.Write([int]0); $writer.Write([int]($n * $n * 4 + $maskStride * $n)); $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0)
    for ($y = $n - 1; $y -ge 0; $y--) { $writer.Write($pixels, $y * $n * 4, $n * 4) }
    for ($y = $n - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $maskStride
        for ($x = 0; $x -lt $n; $x++) {
            if ($pixels[($y * $n + $x) * 4 + 3] -eq 0) { $row[$x -shr 3] = $row[$x -shr 3] -bor (0x80 -shr ($x -band 7)) }
        }
        $writer.Write($row)
    }
    $writer.Flush()
    return , $stream.ToArray()
}

function ConvertTo-Png([System.Drawing.Bitmap] $bitmap) {
    $stream = New-Object IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $stream.ToArray()
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('stk-icon-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    # 全部の大きさを 1 枚のページに並べて 1 回で撮り、切り出す（ヘッドレスの窓は小さくすると大きさが当てにならない）。
    $svg = [IO.File]::ReadAllText($svgPath, [Text.Encoding]::UTF8)
    $data = 'data:image/svg+xml;base64,' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($svg))
    $cells = @()
    $left = 0
    foreach ($size in $sizes) {
        $cells += [pscustomobject]@{ Size = $size; Left = $left }
        $left += $size + 8
    }
    $width = $left
    $height = ($sizes | Measure-Object -Maximum).Maximum
    $images = ($cells | ForEach-Object { "<img src=`"$data`" style=`"position:absolute;left:$($_.Left)px;top:0;width:$($_.Size)px;height:$($_.Size)px`">" }) -join ''
    $page = Join-Path $work 'page.html'
    [IO.File]::WriteAllText($page, "<!doctype html><html><body style=`"margin:0;background:transparent`">$images</body></html>", (New-Object Text.UTF8Encoding $false))

    # 別の user-data-dir で立てる。手元で Edge が開いていると、同じ置き場ではそちらへ渡されて何も撮られない。
    $shot = Join-Path $work 'sheet.png'
    $arguments = @(
        '--headless', '--disable-gpu', '--hide-scrollbars', '--no-first-run', '--force-device-scale-factor=1',
        '--default-background-color=00000000', "--user-data-dir=`"$(Join-Path $work 'profile')`"",
        "--window-size=$width,$height", "--screenshot=`"$shot`"", ([Uri]$page).AbsoluteUri
    )
    $process = Start-Process -FilePath $Edge -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if (-not (Test-Path -LiteralPath $shot)) { throw "Edge が撮れなかった（終了コード $($process.ExitCode)）" }

    $sheet = New-Object System.Drawing.Bitmap $shot
    $entries = @()
    try {
        if ($sheet.Width -ne $width -or $sheet.Height -ne $height) { throw "撮れた大きさが $($sheet.Width)x$($sheet.Height)（$($width)x$($height) のはず）" }
        foreach ($cell in $cells) {
            $n = $cell.Size
            $bitmap = $sheet.Clone((New-Object System.Drawing.Rectangle $cell.Left, 0, $n, $n), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                # 透けた角と白いハートで、撮れたのがこの絵で、地が透けていることを確かめる。
                $corner = $bitmap.GetPixel(0, 0)
                $middle = $bitmap.GetPixel([int]($n / 2), [int]($n / 2))
                if ($corner.A -ne 0) { throw "$n の角が透けていない（alpha $($corner.A)）" }
                if ($middle.R -lt 240 -or $middle.G -lt 240 -or $middle.B -lt 240) { throw "$n の真ん中が白くない（$middle）" }

                $bytes = if ($n -ge 256) { ConvertTo-Png $bitmap } else { ConvertTo-IconBmp $bitmap }
                $entries += [pscustomobject]@{ Size = $n; Bytes = $bytes }
            } finally {
                $bitmap.Dispose()
            }
        }
    } finally {
        $sheet.Dispose()
    }

    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter $stream
    $writer.Write([int16]0); $writer.Write([int16]1); $writer.Write([int16]$entries.Count)
    $offset = 6 + 16 * $entries.Count
    foreach ($entry in $entries) {
        $dimension = if ($entry.Size -ge 256) { 0 } else { $entry.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([int16]1); $writer.Write([int16]32); $writer.Write([int]$entry.Bytes.Length); $writer.Write([int]$offset)
        $offset += $entry.Bytes.Length
    }
    foreach ($entry in $entries) { $writer.Write($entry.Bytes) }
    $writer.Flush()
    [IO.File]::WriteAllBytes($icoPath, $stream.ToArray())
    Write-Host "wrote $icoPath ($($stream.Length) bytes; $($sizes -join ', '))"
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($Preview) {
    # 見本はできた .ico を Windows に読ませて描く（窓やエクスプローラーと同じ道。System.Drawing.Icon は 256 の PNG を読まない）。
    # 上の 2 段は実寸、下の 2 段は 48 までを 4 倍。
    Add-Type -Namespace SpoitableIcon -Name Native -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
[DllImport("user32.dll")]
public static extern bool DestroyIcon(IntPtr icon);
'@
    $zoomed = $sizes | Where-Object { $_ -le 48 }
    $pad = 12
    $actualWidth = ($sizes | Measure-Object -Sum).Sum + $pad * ($sizes.Count + 1)
    $zoomWidth = ($zoomed | Measure-Object -Sum).Sum * 4 + $pad * ($zoomed.Count + 1)
    $rowHeight = 256 + $pad * 2
    $zoomHeight = 48 * 4 + $pad * 2
    $sheet = New-Object System.Drawing.Bitmap ([int][Math]::Max($actualWidth, $zoomWidth)), ([int]($rowHeight * 2 + $zoomHeight * 2))
    $graphics = [System.Drawing.Graphics]::FromImage($sheet)
    try {
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
        $backgrounds = [System.Drawing.Color]::White, [System.Drawing.ColorTranslator]::FromHtml('#0a0a0a')
        $top = 0
        foreach ($zoom in 1, 4) {
            $list = if ($zoom -eq 1) { $sizes } else { $zoomed }
            $height = if ($zoom -eq 1) { $rowHeight } else { $zoomHeight }
            foreach ($background in $backgrounds) {
                $brush = New-Object System.Drawing.SolidBrush $background
                $graphics.FillRectangle($brush, 0, $top, $sheet.Width, $height)
                $brush.Dispose()
                $x = $pad
                foreach ($size in $list) {
                    # IMAGE_ICON・LR_LOADFROMFILE。実寸の絵を取り出してから広げる（4 倍で読ませると大きい方の絵を縮めてくる）。
                    $handle = [SpoitableIcon.Native]::LoadImage([IntPtr]::Zero, $icoPath, 1, $size, $size, 0x10)
                    if ($handle -eq [IntPtr]::Zero) { throw ".ico から $size が読めない" }
                    try {
                        $cell = [System.Drawing.Icon]::FromHandle($handle).ToBitmap()
                        $graphics.DrawImage($cell, $x, $top + $pad, $size * $zoom, $size * $zoom)
                        $cell.Dispose()
                    } finally {
                        [void][SpoitableIcon.Native]::DestroyIcon($handle)
                    }
                    $x += $size * $zoom + $pad
                }
                $top += $height
            }
        }
    } finally {
        $graphics.Dispose()
    }
    $sheet.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose()
    Write-Host "wrote $Preview"
}
