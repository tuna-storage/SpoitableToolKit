# SpoitableToolKit

日本語 | [English](README_EN.md)

VRChat と一緒に使う、Windows 用のアプリです。
デスクトップの画面と、SteamVR のオーバーレイの両方で使えます。
心拍計の心拍数をアバターへ送信したり、再生に失敗した動画の URL を再生できる形にしたり、VRChat のログを一覧にしたりできます。
機能はモジュールとして追加できます。

## できること

### 心拍

Bluetooth (BLE) の心拍計と、Apple Watch のアプリ Health Data Server (HDS) から心拍数を受信します。
受信した心拍数は、次のように使えます。

- VRChat のアバターへ OSC で送信する (パラメータ名はプリセットの VRCHR か VRCOSC から選べます)
- 記録して、統計とグラフで振り返る (CSV にエクスポートできます)
- WebSocket で配信して、配信ソフトのオーバーレイに表示する

### 動画 URL

VRChat で動画の再生に失敗すると、その URL を再生できる形に解決して、クリップボードにコピーします。
解決には yt-dlp を使用します。
この PC に yt-dlp を置きたくない場合は、自分で用意したサーバーに解決を任せることもできます ([docs/videourl-server.md](docs/videourl-server.md))。

### VRChat のログ

VRChat のログから、入室、退室、ワールドの移動、動画の再生などの出来事を一覧にします。
一覧は検索と絞り込みができ、日時を指定して遡ることもできます。

### VR

SteamVR の中で手首を見ると、ショートカットバーが表示されます。
ショートカットバーでは心拍数と時刻を確認でき、ショートカットからアプリの画面を開けます。
開いた画面は VR ウィンドウとして、手首の上に表示するか、目の前の空間に配置するかを選べます。

表示言語は日本語と英語に対応していて、テーマはライトとダークから選べます。

## ダウンロード

[Releases](../../releases) からインストーラー (`SpoitableToolKit-Setup.exe`) をダウンロードできます。
64 bit の Windows 専用で、動作は Windows 11 で確認しています。

インストールしたアプリは自動で更新を確認し、新しいバージョンがあると画面の上に通知を表示します。
更新のチャンネルは、設定で Stable と Nightly (開発中のバージョン) から選べます。

インストーラーにはコード署名をしていないため、初めて開くときに Windows の SmartScreen の確認が表示される場合があります。

設定と心拍の記録は `%LOCALAPPDATA%\SpoitableToolKit` に保存されます。
アプリのインストール先とは別のフォルダなので、アンインストールしても削除されません。

## 使い方

心拍計は、設定の「心拍」で選びます。
空欄の場合は、最初に見つかった心拍計に接続します。

アバターへ送信するには、VRChat の OSC を ON にしてください。
送信先のポートは設定の「OSC」で変更できます (既定は 9000 です)。

VR の機能は、SteamVR が起動していれば使えます。
アプリより後に SteamVR を起動した場合も、アプリが SteamVR を検出した時点で表示されます。

## 通信について

アプリがこの PC の外と通信するのは、次の場合です。

- アプリの更新の確認：このリポジトリの Releases (GitHub) を確認します。
- モジュールの追加と更新の確認：モジュールのリポジトリ (GitHub) から、リリースやソースをダウンロードしたり、新しいリリースを確認したりします。
- 動画 URL の解決：初回に yt-dlp と deno を GitHub からダウンロードし、その後は yt-dlp の新しいバージョンを 1 日 1 回まで確認します (PATH にある yt-dlp を使う場合は確認しません)。解決するときは、yt-dlp が動画のサイトに問い合わせます。外部サーバーを使用する設定にした場合は、動画の URL がそのサーバーへ送信されます。
- モジュールのソースからの Build：必要なパッケージを NuGet からダウンロードします。.NET SDK が見つからない場合は、確認のあとで Microsoft からダウンロードします。

心拍の OSC と配信は、この PC の中だけで行います。
Apple Watch (HDS) から受信する設定を ON にした場合だけ、LAN からの受信を待ち受けます。
追加したモジュールの通信は、それぞれのモジュールによって異なります。

## モジュール

モジュールは、アプリの「モジュール」で GitHub のリポジトリの URL を入力して追加します。
リポジトリにリリースがある場合はその zip をインストールし、無い場合はソースをダウンロードしてこの PC で Build します (Build の前に確認を表示します)。

モジュールはアプリと同じ権限で動作するため、信頼できるものだけを追加してください。

モジュールを作るための SDK は、アプリに同梱しています。
最小限のモジュールの例は [SpoitableToolKit-SampleModule](https://github.com/tuna-storage/SpoitableToolKit-SampleModule) にあります。

## Build

Build には .NET 10 SDK が必要です。
画面は CefSharp (Chromium) で描画しています。

```powershell
dotnet build SpoitableToolKit.slnx
dotnet test tests/SpoitableToolKit.Tests
dotnet test tests/SpoitableToolKit.E2E   # アプリを起動して画面を操作する試験
```

画面の文言は、各プロジェクトの `locales/` にあります。
インストーラーの作り方は [docs/release.md](docs/release.md) にあります。

## ライセンス

[MIT](LICENSE)

同梱しているライブラリとそのライセンスは [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) にあり、アプリの設定の「ライセンス」からも確認できます。
