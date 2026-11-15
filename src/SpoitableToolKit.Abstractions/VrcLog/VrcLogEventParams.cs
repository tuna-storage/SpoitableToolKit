namespace SpoitableToolKit.Abstractions.VrcLog;

/// <summary>
/// 意味づけに成功したログエントリ。認識できなかった行は <see cref="VrcLogLine"/> のまま流れる。
/// <para>
/// 新しい種別を足すときはここに sealed record を 1 つ増やし、パーサ側に対応を書く。
/// 消費側は型スイッチで受ける。
/// </para>
/// </summary>
public abstract record VrcLogEventParams
{
    /// <summary>このイベントの元になった行。生テキストや未パースの情報はここから辿れる。</summary>
    public required VrcLogLine Line { get; init; }

    public DateTimeOffset Timestamp => Line.Timestamp;

    /// <summary>UI やストアで使う短い種別名。型名から機械的に導く。</summary>
    public string Kind => GetType().Name;
}
