namespace SpoitableToolKit.Abstractions.Ui;

/// <summary>
/// 状態の重さ。<b>実際の色はシェルが決める</b>ので、テーマを変えれば一緒に変わる。
/// </summary>
public enum StatusState
{
    /// <summary>止まっている・繋がっていない。</summary>
    Off,

    /// <summary>動いている。</summary>
    Ok,

    /// <summary>気に留めてほしい。</summary>
    Warn,

    /// <summary>壊れている。</summary>
    Bad,
}
