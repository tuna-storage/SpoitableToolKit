using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Host.AppLog;

/// <summary>ソフトウェア自身のログ 1 行。VRChat のログ（<see cref="Abstractions.VrcLog.VrcLogLine"/>）とは別物。</summary>
/// <param name="Category">
/// <c>ILogger</c> のカテゴリ。画面では末尾だけを出すが、ファイルには丸ごと残す（名前空間が違う同名の型を見分けるため）。
/// </param>
/// <param name="ThreadId">出したスレッド。CEF・tail のループ・UiPump が別スレッドなので、競合を追うのに要る。</param>
/// <param name="Seq">
/// 通し番号。<see cref="AppLogSink"/> が振る。起動をまたいで続ける（起動ごとに 1 から振ると、読み戻した前の起動の行と番号が被り、画面の重複弾きに消される）。
/// 画面は getStatus の追い付きと push の 2 経路で同じ行を受けうるので、番号で重複を弾く。折り畳みの開閉を覚える鍵でもある。
/// </param>
internal sealed record AppLogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    string? Exception,
    int ThreadId,
    long Seq = 0)
{
    /// <summary>どの起動で出た行か（その起動の時刻のエポックミリ秒）。<see cref="AppLogSink"/> が振る。読み戻した行は前の起動の印のままで、画面は隣と印が違う所に「ここで起動」を挟む。</summary>
    public long Run { get; init; }

    public string Short => Level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    public string ShortCategory =>
        Category.LastIndexOf('.') is var dot and >= 0 && dot + 1 < Category.Length
            ? Category[(dot + 1)..]
            : Category;
}
