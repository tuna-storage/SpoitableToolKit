using SpoitableToolKit.Host.Modules.Packaging;

namespace SpoitableToolKit.Host.Modules.Loading.Models;

internal enum DependencyProblemKind
{
    /// <summary>参照しているものが、共有にも自分のフォルダにも本体にも無い。</summary>
    Missing,

    /// <summary>共有にあるが、載せた版が参照している版より古い。</summary>
    TooOld,

    /// <summary>
    /// <b>本体の API</b>（<see cref="ModuleDependencies.AppApi"/>）が、参照している版より古い（モジュールが、この本体より新しい SDK で作られている）。
    /// 直すのはアプリの更新。API の dll はモジュールが持ち込めない（持ち込むと、本体のものと型が二重になる）。
    /// </summary>
    HostTooOld,

    /// <summary>
    /// <b>本体の API</b> の、この本体が読めるより古い版を参照している（モジュールが、古い SDK で作られている。<see cref="ModuleManifest.MinimumSdk"/>）。
    /// 直すのはモジュールの作り直し（新しい SDK で）。<paramref name="Available"/> には読める版の下限が入る。
    /// </summary>
    SdkTooOld,
}
