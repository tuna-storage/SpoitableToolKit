# 第三者のライセンスの一覧の作り方

repo の根の `THIRD-PARTY-NOTICES.md` は、アプリの出力に入る第三者のものの一覧（名前・版・ライセンス・著作権の行・ライセンスの全文）。
build で出力の根に置かれ、設定の「ライセンス」（オープンソースライセンスの画面）の「サードパーティ」がそのまま出す。
飾りの無い文字のまま読める形で書く（画面は Markdown を描かず、そのまま出す）。

試験 `ThirdPartyNoticesTests` が、本体と同梱モジュールの `obj/project.assets.json` と突き合わせる。

- assets のパッケージ（と `downloadDependencies`）が一覧の `- NuGet:` の行に全部ある
- 一覧の `- NuGet:` の行に、もう入っていないものが無い
- 版の先頭の数（major）が同じ。本体と hrs は `10.*` で引くので、末尾の数までは見ない
- `- NuGet:` の行がある節は、`- License:` と `- Copyright:` の行を持つ
- パッケージが出力に置く native の dll・exe（assets の `native`・`runtimeTargets`・`contentFiles`。CEF の dll など）の名前が、どれも一覧にある。
  パッケージと同じ名前で始まるもの（`CefSharp.*`）は除く。パッケージの build の target が置くファイル（CefSharp の `Ijwhost.dll` など）はここに出てこないので、
  出力の dll を見て手で足す

## 依存を足した・版を上げたとき

1. build する（assets が新しくなる）。
2. 試験を回す。落ちた文に、足す・消す・直すパッケージと版が出る。

   ```powershell
   dotnet test tests/SpoitableToolKit.Tests/SpoitableToolKit.Tests.csproj --no-build --filter "FullyQualifiedName~ThirdPartyNoticesTests"
   ```

3. パッケージの nuspec と、中のライセンスのファイルを見る。下は、assets にあるものを 1 行ずつ出す。

   ```powershell
   $assets = Get-Content -Raw -Encoding utf8 src/SpoitableToolKit.Host/obj/project.assets.json | ConvertFrom-Json
   foreach ($lib in $assets.libraries.PSObject.Properties) {
     if ($lib.Value.type -ne 'package') { continue }
     $dir = Join-Path "$env:USERPROFILE\.nuget\packages" $lib.Value.path
     [xml]$x = Get-Content -Raw -Encoding utf8 (Get-ChildItem $dir -Filter *.nuspec).FullName
     $m = $x.package.metadata
     $files = (Get-ChildItem $dir -File | Where-Object Name -match '^(LICENSE|NOTICE|THIRD)').Name -join ','
     "{0} | {1} {2} | {3} | {4}" -f $lib.Name, $m.license.type, $m.license.'#text', $m.copyright, $files
   }
   ```

   同梱モジュール（`src/SpoitableToolKit.Modules/*/obj/project.assets.json`）も同じように見る。

4. 一覧に書く。
   - 同じ作り手の同じライセンスのパッケージは 1 つの節にまとめ、`- NuGet:` の行に `Id 版` を `, ` で並べる。
   - 著作権の行は nuspec の `copyright` と、パッケージ（無ければ本家の repo）の LICENSE の行。
   - ライセンスの文は、パッケージの中のファイル（`license type="file"`）か本家の repo の LICENSE から写す。覚えで書かない。
     MIT と Apache 2.0 は末尾の「License texts」に 1 回だけ置き、節からはそこを指す。文に著作権の行が入っているもの（BSD など）は節の中に全文を置く。
   - 版だけが上がったなら、`- NuGet:` の行の版と、節の中の版（CEF の `Version:` など）を直す。

## 手で補っているもの

パッケージから取れないので、本家を見て書いた。版を上げたら見直す。

- **OpenVR**（`VolcanicArts.Libs.OpenVR`）: パッケージにライセンスが無い。中身は Valve の OpenVR SDK なので、
  その版の `https://github.com/ValveSoftware/openvr/blob/v<版>/LICENSE` を写した。
- **SQLite**: `e_sqlite3.dll` の中身。パブリックドメイン（`https://www.sqlite.org/copyright.html`）。
- **Windows SDK**（MIT ではない。Windows SDK の使用許諾 `https://aka.ms/WinSDKLicenseURL` の Distributable Code として、変更せずに配る）:
  - `modules/hrs/Microsoft.Windows.SDK.NET.dll`・`modules/hrs/WinRT.Runtime.dll`: `Microsoft.Windows.SDK.NET.Ref` のパッケージの `lib/net8.0/` のもの
    （hrs の TFM が `net10.0-windows10.0.19041.0` なので入る。hrs の deps.json では `runtimepack.Microsoft.Windows.SDK.NET.Ref`、中身もパッケージのものとバイト単位で同じ）。
    nuspec は `licenseUrl` だけ。REDIST の一覧（`https://learn.microsoft.com/en-us/legal/windows-sdk/redist`）の「Microsoft.Windows.SDK.NET.Ref」の節が、
    WinRT の API を呼ぶためにプログラムの一部として変更せずに配ってよい、と名指しで許している。C#/WinRT のソースは MIT だが、配っている dll の条件はパッケージの許諾。
  - `d3dcompiler_47.dll`・`dxil.dll`: CEF と一緒に出力に入る Microsoft のファイル。REDIST の一覧の Direct3D の節にある。chrome://credits には載っていない。
  - `dxcompiler.dll`: 名前は同じ Direct3D の節にあるが、出力のものは SDK のものではない（ファイルの説明が「Google Dawn Custom Build」。
    Chromium のために DirectX Shader Compiler のソースから作ったもの）。ソースのライセンスは chrome://credits の「DirectX-Shader-Compiler」。
    一覧では Windows SDK の節の下に、その旨を書いている。
  - 使用許諾の配布の条件（エンドユーザーにも同じだけ守る条件を求める）への手当てとして、節に「These files are not under the MIT License …」の文を置いている。
- **.NET**: self-contained で入る runtime と、`SpoitableToolKit.Host.exe`（apphost）と、CefSharp が持ってくる `Ijwhost.dll`（.NET の C++/CLI の host）。runtime pack の `LICENSE.TXT`。
  第三者の告知 `THIRD-PARTY-NOTICES.TXT` は Microsoft.Extensions のパッケージのものと同じ中身なので、出力の `licenses/dotnet/` に 1 つ置く。
  MIT でない部品（`https://github.com/dotnet/core/blob/main/license-information-windows.md`）のうち、self-contained の publish の出力にあるのは
  `Microsoft.DiaSymReader.Native.amd64.dll`（.NET Library License）だけ（`dotnet publish -c Release -p:SelfContained=true` の出力で確かめた）。
  WPF の `PresentationNative_cor3.dll`・`vcruntime140_cor3.dll`・`wpfgfx_cor3.dll`・`D3DCompiler_47_cor3.dll` は入らない（WinForms だけを使うため）。
  `coreclr.dll` は single-file で publish したときだけ .NET Library License になる。publish の形を変えたら、出力をこの一覧と見比べ直す。
- **Velopack**: `Update.exe`・`Setup.exe`。本家の LICENSE。中の Rust の crate は下の「Velopack の Rust の crate」。
- **Lucide・Feather**: 画面の線のアイコン（`index.html` の `<symbol>` など）の一部が、これらの形を元にしている可能性が高い（出どころの記録は無い）。
  安全側で両方を載せている。文は本家の LICENSE（Lucide は Feather 由来の部分の MIT を含む全文）。
  アプリのアイコン（`Assets/app-icon.svg`。exe・窓・Setup.exe）のハートは Lucide の heart（Feather 由来の一覧には無い）。
- **Chromium**: 中の部品のライセンスは数が多い（chrome://credits に 700 以上）ので、一覧には書かない。
  設定の「ライセンス」の「Chromium のライセンス」が、アプリの中の Chromium の chrome://credits を別の窓で開く（同じ版の中身が必ず出る）。
  CEF と一緒に入る dll のうち、`chrome_elf.dll` は Chromium、`libEGL.dll`・`libGLESv2.dll` は ANGLE（credits では「Almost Native Graphics Layer Engine」）、
  `vk_swiftshader.dll` は SwiftShader、`vulkan-1.dll` は Vulkan Loader Components、`dxcompiler.dll` は DirectX-Shader-Compiler として、どれも credits にある。
- **LGPL の部分**（Blink の一部・FFmpeg。どちらも `libcef.dll` の中。credits の「WebKit」「ffmpeg」）: Chromium の節に、ソースの場所と「差し替えてよい」の文を置いている。
  CEF の版を上げたら、次を新しい版に直す:
  - CEF の版と commit: パッケージの `README.txt`（`CEF Version`・`CEF URL` の `@` の後ろ）。branch は CEF の repo で、その commit が入っている branch（Chromium の build の番号と同じ名前）
  - Chromium の版と commit: 同じ `README.txt` の `Chromium Version`・`Chromium URL`。tag は `https://chromium.googlesource.com/chromium/src/+/refs/tags/<版>`
  - FFmpeg の構成: その tag の `DEPS` の `ffmpeg_revision` の、`chromium/third_party/ffmpeg` の `chromium/config/Chromium/win/x64/config.h`
    （`FFMPEG_LICENSE`・`CONFIG_GPL`・`CONFIG_GPLV3`・`CONFIG_NONFREE`）。proprietary codecs を入れた build は `Chrome` の方の config を使うので、
    アプリの中で H.264・AAC が再生できないこと（`canPlayType('video/mp4; codecs="avc1.42E01E"')` が空）も見て、`Chromium` の方の config だと確かめる。
- **アプリが必要になったときにダウンロードするもの**（yt-dlp・Deno・.NET SDK・モジュールの restore で入るパッケージ）:
  配っていないので `- NuGet:` の行は無い。取りに行く先を変えたら直す。

## Velopack の Rust の crate

`Update.exe`・`Setup.exe` の土台は Velopack の Rust のコードで、Velopack は中の crate のライセンスの一覧を配っていない
（vpk の `vendor/THIRD_PARTY_NOTICES.md` は vpk の手元の道具の分だけで、exe の中にもライセンスの文は無い）。
一覧の「Velopack: Rust crates in Update.exe and Setup.exe」の節は、`tools/licenses/velopack-crates.ps1` で作る。
`Cargo.lock` は repo に無いので、一覧と突き合わせる試験は無い。Velopack の版を上げたら作り直す。

1. 新しい版の Velopack のパッケージの nuspec の `<repository … commit="…">` を見る（`~/.nuget/packages/velopack/<版>/velopack.nuspec`）。
2. 空のフォルダを決めて、作る（cargo が要る。rustup の `~/.cargo/bin` を PATH に）。

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/licenses/velopack-crates.ps1 -Commit <commit> -Work <空のフォルダ> -Out <空のフォルダ>\crates.md
   ```

   - その commit を `-Work` の中へ取ってきて、`cargo tree`（Velopack の Windows の build と同じ形: workspace から python を除き、features `windows`、target `x86_64-win7-windows-msvc`）で
     `velopack_bins`（`Update.exe`・`Setup.exe` を作る package）から normal の依存を辿る。build・dev の依存と proc macro は外す（exe に入らない）。
     Linux・macOS だけの依存も target で外れる。package の単位なので、同じ package の `stub.exe`・`testapp.exe` だけが使う crate も入りうる
   - 落としてきたものは信頼できないデータとして扱う。走らせるのは git の fetch・checkout と `cargo tree` だけ（crate の build script・proc macro は走らない）。
     cargo は `-Work` の中の空のフォルダから `--manifest-path` で呼ぶ（Velopack の repo の `.cargo/config.toml` を読ませない）。`CARGO_HOME` も `-Work` の中
   - 選べるライセンス（`MIT OR Apache-2.0` など）は、MIT・Apache-2.0・Unicode-3.0・BSD-3-Clause・ISC・Zlib… の順で先にあるものを使う。
     著作権の行は、そのライセンスのファイルの `Copyright`・`(c)`・`©` で始まる行。無ければ Cargo.toml の `authors`。
     MIT・Apache 2.0 の全文は一覧の末尾のものを使い、ほかのライセンスは crate ごとに（Unicode は全部同じ文なので 1 つ）全文を置く
3. 書き出された中身で、一覧の「- Crates:」の行から次の `### Lucide` の手前までを置き換え、節の頭の commit と Velopack の版を直す。
4. MPL-2.0 など、選べない新しいライセンスの crate が入ってきたら、スクリプトは止まる（`no known license`）。そのライセンスの扱いを決めて、スクリプトの `$preference` と `$titles` に足す。
   MPL-2.0 なら、その crate のソースの在りか（`https://crates.io/crates/<名前>/<版>`）も書く。

## 出力に置くもの

`src/SpoitableToolKit.Host/SpoitableToolKit.Host.csproj` の `SpoitableLicenseFile` が、出力（と publish）に置く:

| 出力 | 元 |
|---|---|
| `LICENSE` | repo の根 |
| `THIRD-PARTY-NOTICES.md` | repo の根 |
| `licenses/CefSharp/LICENSE` | CefSharp.WinForms.NETCore のパッケージ |
| `licenses/CEF/LICENSE.txt` | chromiumembeddedframework.runtime のパッケージ |
| `licenses/dotnet/THIRD-PARTY-NOTICES.TXT` | Microsoft.Extensions.Logging のパッケージ |

モジュールの SDK のパッケージ（`SpoitableToolKit.Sdk`）は、ライセンスを `MIT`（式）で持ち、
根に `LICENSE` と `THIRD-PARTY-NOTICES.md`（`src/SpoitableToolKit.Sdk/THIRD-PARTY-NOTICES.md`。中に入れている Microsoft.Extensions.Logging.Abstractions の分）を入れる。
