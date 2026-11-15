# SDK の形を比べる相手

ここには、**本体が読めるいちばん古い SDK**（`ModuleManifest.MinimumSdk`）のパッケージを 1 つだけ置く。

SDK（`SpoitableToolKit.Abstractions`）を pack するたびに、.NET の package validation（APICompat）がこれと比べる。
アプリの build も毎回 SDK を pack している（Host の `BundleSdkPackage`）ので、SDK の形を壊すとアプリの build が落ちる。

| 変更 | 古いモジュール | 比べた結果 |
|---|---|---|
| 新しい型・新しい interface | 動く | 通る |
| interface に、既定の本体付きで member を足す | 動く | 通る |
| interface に、既定の本体なしで member を足す | モジュールが実装していれば、読み込みで `TypeLoadException` | 落ちる（`CP0006`） |
| 型や member を消す・名前や引数を変える・record の位置引数を足す | 落ちる | 落ちる |

## 落ちたら

まず、壊さずに済む形を探す。

- interface の新しい member には既定の本体を付ける。**本体だけが実装する interface（`IModuleHost`・`IEventHub` など）でも同じ** —
  比べる道具には、モジュールが実装していないことは分からない。既定の本体は、今ある member を使って書けることが多い
  （例: `IEventHub.FireCollectMany` は `FireCollect`、`FireWithResult` は `FireCancellable` で書いてある）。
- 引数を足したいときは、前の形を残したまま新しい形を足す。

**`CompatibilitySuppressions.xml` に壊した所（CP…）を足して抑えない。** MinimumSdk を上げずに壊せてしまう
（`-p:ApiCompatGenerateSuppressionFile=true` は、そこに今の差を全部書き込む）。抑えてよいのは PKV004 だけ（パッケージは `ref/` だけで `lib/` を持たない — わざと）。

## わざと壊すとき

MinimumSdk を上げるのは、SDK の形を壊したときだけ。

1. `Directory.Build.props` の `Version` を上げる。
2. `src/SpoitableToolKit.Host/Modules/Packaging/ModuleManifest.cs` の `ModuleManifest.MinimumSdk` を同じ版にする。
3. 比べる相手を作り直す。

   ```
   dotnet pack src/SpoitableToolKit.Abstractions -c Release -p:SpoitableRefreshSdkBaseline=true
   ```

   このときだけ比べない。いまの SDK を詰めて、ここの古いものと入れ替える。

**最初の SDK を外に出すまで**は、1 と 2 は飛ばしてよい（版はそのまま）。まだ誰も古い SDK でモジュールを作っていないので、3 だけで済む。

版とここのパッケージの食い違いは、試験 `SdkBaselineTests` が見る。
