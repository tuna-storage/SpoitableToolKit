namespace SpoitableToolKit.Abstractions.Notifications;

/// <summary>
/// 通知の種類。画面の<b>角四角の色とアイコン</b>になる（右下のトースト・手首の通知・頭に付いてくる通知で同じもの）。
/// <para>
/// <b>モジュールが自分で作ってよい</b>。
/// 登録の手続きは無く、通知と一緒に渡すだけでよい:
/// <code>
/// static readonly NotifyKind Heart = new("hrs.heart", NotifyTone.Rose, "M19 14c1.5-1.5 3-3.2 3-5.5A5.5 ...");
/// host.Notifications.PushToast(Heart, "心拍計に繋がりました");
/// </code>
/// </para>
/// <para>
/// <b>アイコンは SVG の <c>path</c> の <c>d</c> だけ</b>（24×24 の枠、線で描く、塗りなし）。
/// HTML は渡せない — シェルは別 origin のモジュールに HTML を書かせない。
/// パスの字（数字・空白・<c>MmLlHhVvCcSsQqTtAaZz</c>・<c>.,-</c>）以外が混ざっていたら、ホストは<b>情報の印に落とす</b>。
/// </para>
/// </summary>
/// <param name="Id">種類の名前。<b>モジュールの Id を頭に付ける</b>こと（<c>hrs.heart</c>）。手首の通知の絞り込みに使う。</param>
/// <param name="Tone">角四角の色の系統。</param>
/// <param name="IconPath">アイコンの SVG パス（<c>d</c>）。</param>
public sealed record NotifyKind(string Id, NotifyTone Tone, string IconPath)
{
    /// <summary>ふつうの知らせ（既定）。i の字。</summary>
    public static readonly NotifyKind Info = new("info", NotifyTone.Gray,
        "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18ZM12 11v5M12 7.5v.01");

    /// <summary>済んだ（コピーした・書き出した）。チェックの印。</summary>
    public static readonly NotifyKind Done = new("done", NotifyTone.Green,
        "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18ZM8 12.5l2.7 2.7L16 9.5");

    /// <summary>気に留めてほしい。三角の印。</summary>
    public static readonly NotifyKind Warn = new("warn", NotifyTone.Amber,
        "M10.3 4.2 2.6 17.5A2 2 0 0 0 4.3 20.5h15.4a2 2 0 0 0 1.7-3L13.7 4.2a2 2 0 0 0-3.4 0ZM12 9.5v4M12 17v.01");

    /// <summary>失敗した。丸に ✕。</summary>
    public static readonly NotifyKind Error = new("error", NotifyTone.Red,
        "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18ZM9 9l6 6M15 9l-6 6");

    /// <summary>取ってきている・読み込んでいる。下向きの矢印。</summary>
    public static readonly NotifyKind Download = new("download", NotifyTone.Blue,
        "M12 4v11M7.5 10.5 12 15l4.5-4.5M5 19.5h14");

    /// <summary>
    /// パスとして使ってよい字だけか。<b>ホストはこれが偽なら <see cref="Info"/> のアイコンに落とす。</b>
    /// </summary>
    public static bool IsSafePath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.Length <= 2048
        && path.All(c => char.IsAsciiDigit(c) || c is ' ' or '.' or ',' or '-'
                         || "MmLlHhVvCcSsQqTtAaZz".Contains(c));
}
