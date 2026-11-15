namespace SpoitableToolKit.Abstractions.VrcLog;

/// <summary>ストアから読み戻した 1 件。型階層には戻さず、JSON のまま返す。</summary>
public sealed record StoredLogEvent
{
    public required long Id { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    /// <summary><see cref="VrcLogEventParams.Kind"/> と同じ（型名）。</summary>
    public required string Kind { get; init; }

    /// <summary>イベント固有のフィールドを JSON にしたもの。</summary>
    public required string Payload { get; init; }

    /// <summary>元になった行の本文。</summary>
    public required string Message { get; init; }

    /// <summary>
    /// 読み出し元の output_log のファイル名。
    /// <b>required にしない</b> — 既に配ってある型なので、足すだけで壊さない形にしておく。
    /// </summary>
    public string? SourceFile { get; init; }
}
