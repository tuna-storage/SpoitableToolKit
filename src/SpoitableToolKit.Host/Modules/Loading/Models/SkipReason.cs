namespace SpoitableToolKit.Host.Modules.Loading.Models;

internal enum SkipReason
{
    /// <summary><c>.disabled</c> の印、または本体の設定「読み込まないモジュール」。</summary>
    Disabled,

    /// <summary>dll はあるのに入口が見つからない。</summary>
    NoEntry,

    /// <summary>同じ Id が先の探索ルートにもあり、そちらが勝った。</summary>
    Shadowed,

    /// <summary>参照しているものが足りない（<see cref="SkippedModule.Problem"/> に中身）。</summary>
    MissingDependency,

    /// <summary>読み込もうとして落ちた（dll が壊れている・<c>ISpoitableModule</c> が無い・作れない）。<see cref="SkippedModule.Detail"/> に中身。</summary>
    LoadFailed,
}
