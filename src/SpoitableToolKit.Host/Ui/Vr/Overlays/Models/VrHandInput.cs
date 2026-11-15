namespace SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

/// <summary>
/// 片手のコントローラーの今。SteamVR Input（action manifest、<c>vr/actions.json</c>）から読むので、割り当ては SteamVR のコントローラー設定画面で変えられる。
/// 読んでも VRChat から入力を取り上げない（トリガーもグリップも VRChat に素通しで届く）。
/// スティックは読まない — VRChat の移動・回転にも素通しで効くので、窓を巻くたびに歩いてしまう。巻くのはトリガーを引いたまま動かす。
/// </summary>
/// <param name="Aim">狙いの姿勢。-Z が指している向き。割り当てが無い・追えていなければ null。先端のずれは SteamVR が持っている（<c>pose/tip</c>）。</param>
internal readonly record struct VrHandInput(VrMatrix? Aim, bool Click, bool Grab);
