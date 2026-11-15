namespace SpoitableToolKit.Abstractions.VrcLog;

/// <summary>
/// output_log の 1 エントリを正規化したもの。
/// <para>
/// 認識できたかどうかに関わらず、すべての行がこれになる。構造化に失敗しても情報を落とさないための土台。
/// スタックトレースのようにタイムスタンプを持たない継続行は、直前のエントリの
/// <see cref="Message"/> に改行で連結される。
/// </para>
/// </summary>
public sealed record VrcLogLine
{
    /// <summary>ログ行に書かれている時刻（ローカル時刻）。読み取った時刻ではない。</summary>
    public required DateTimeOffset Timestamp { get; init; }

    public required VrcLogLevel Level { get; init; }

    /// <summary><c>[API]</c> の <c>API</c> 部分。タグの無い行は null。</summary>
    public string? Tag { get; init; }

    /// <summary>タグを除いた本文。継続行があれば改行で連結済み。</summary>
    public required string Message { get; init; }

    /// <summary>読み出し元の output_log のファイル名。</summary>
    public required string SourceFile { get; init; }
}
