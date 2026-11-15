# リリースの出し方

配る物は公開の repo（`tuna-storage/SpoitableToolKit`）の GitHub Releases に置く。CI（`.github/workflows/`）は公開の repo でだけ走り、
開発の repo では何もしない。アプリの更新は Velopack で、入っているアプリが公開の repo の Release を見に来る。

## stable

1. `Directory.Build.props` の `<Version>` を上げる（`YYYY.M.BUILD`。例: 2026.11.0 → 2026.11.1、月が変われば 2026.12.0）。
2. 変更点を書く: `CHANGELOG/v<版>_CHANGELOGS.md`（日本語）と `CHANGELOG/v<版>_CHANGELOGS.en.md`（英語）。どちらかが無いと CI が止まる。
   アプリの「変更点」は、画面の言語の方を出す。
3. 公開の repo へ写して、main を push する（main への push では nightly も 1 つ出る）。
4. tag を打って push する:

   ```powershell
   git tag -s v2026.11.1 -m v2026.11.1
   git push <公開の repo の remote> v2026.11.1
   ```

   tag は `v` + `Directory.Build.props` の版と完全に同じでなければ止まる。
5. `Release`（`.github/workflows/release.yml`）が build → publish → Velopack で詰める → 下書きの Release に全部載せてから公開する（latest）。
   入っている stable のアプリは、次の確かめ（起動の 20 秒後と、その後 6 時間おき）で画面の上に知らせを出す。

## nightly

- 公開の repo の main に push するたびに `Nightly`（`.github/workflows/nightly.yml`）が走り、prerelease の Release を 1 つ作る。古いものは消さない。
- 版は `YYYY.M.D-<run_number>.g<hash7>`（日本の日付。例: `2026.11.15-42.gabc1234`）。tag は `nightly-v<版>`（`v` で始めない — stable の契機になる）。
- 変更点は、その push で入った commit の 1 行目を並べたもの。
- ⚠ `nightly.yml` の名前を変えない。`run_number` は workflow ごとの通し番号で、名前を変えると 1 に戻り、同じ日の版の順が崩れる。
- アプリの版だけを nightly の版にする（`-p:AppVersion=…`）。同梱の SDK のパッケージは `Directory.Build.props` の版のまま
  （変えると、SDK を版の数字で指しているモジュールが restore できなくなる）。

## 配る物

| ファイル | 中身 |
|---|---|
| `SpoitableToolKit-Setup.exe`（nightly は `SpoitableToolKit-nightly-Setup.exe`） | インストーラー。`%LOCALAPPDATA%\SpoitableToolKit.App` に入る |
| `SpoitableToolKit.App-<版>-<channel>-full.nupkg`・`releases.<channel>.json`・`RELEASES-<channel>` | アプリが更新に使う。**名前を変えない** |

- データの置き場は今までどおり `%LOCALAPPDATA%\SpoitableToolKit`。インストール先とは別のフォルダなので、アンインストールしても入れ直しても消えない。
  packId を `SpoitableToolKit.App` にしているのはこのため — データのフォルダと同じ名前にすると、そこがインストール先になり、
  Velopack の Setup（入れ直し）とアンインストールがその中身（記録・設定）を消す。
  packId は、出したあとは変えない（変えると別のアプリとして扱われ、入っているアプリに更新が届かない）。
- .NET の runtime は同梱（self-contained）。モジュールを build する .NET SDK は、今までどおり要るときにアプリが落とす。
- Visual C++ の runtime（CefSharp が要る）が無い PC では、Setup が入れる。
- コード署名はしていない（初めて開くと Windows の SmartScreen が出る）。

## 手元で作る

```powershell
dotnet tool restore
./.github/scripts/release-notes.ps1 -Version 2026.11.0 -Out artifacts/notes.md
./.github/scripts/pack.ps1 -Version 2026.11.0 -Channel stable -Notes artifacts/notes.md -Out artifacts/packages
```

- `artifacts/` は git に入らない。作った Setup.exe を走らせると、その PC にインストールされる。
- nightly の版を試すなら `-Version 2026.11.15-1.g0000000 -Channel nightly`。

## アプリの更新の仕組み

- Velopack（NuGet の `Velopack` と CLI の `vpk`。版は Host の csproj と `dotnet-tools.json` で固定）。
- stable は最新の Release（prerelease でないもの）の `releases.stable.json` だけを読む（`releases/latest/download/`）。
  nightly は prerelease を含めた新しい順の 10 件から `releases.nightly.json` を探す。
- 確かめるのはインストールした版だけ。開発の bin や e2e は、どこにも繋がない。
- 試すときは `SPOITABLE_UPDATE_FEED=<フォルダ>`（そのフォルダの `releases.stable.json` を読み、インストールした版のふりをする。
  「更新」では入れる代わりに `applied.txt` に版を書く）と `SPOITABLE_UPDATE_FIRST_CHECK_SECONDS=0`（すぐ確かめる）。
