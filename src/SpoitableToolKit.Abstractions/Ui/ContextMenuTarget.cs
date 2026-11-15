namespace SpoitableToolKit.Abstractions.Ui;

/// <summary>
/// 右クリックされた場所の<b>名乗り</b>。
/// <para>
/// <b>ホストは DOM を見られない。</b> CEF が右クリックのときに渡してくる
/// <c>IContextMenuParams</c> には座標・選択中の文字・入力欄かどうかしか入っておらず、
/// <b>どの要素か・どのボタンかは入っていない</b>（CefSharp 151.3.170 で確認）。
/// そこで<b>ページ側が名乗る</b>。<see cref="Key"/> は <c>data-context</c> 属性を親へ辿って拾う。
/// </para>
/// <para>
/// <b>判断に要る材料も一緒に運ぶ</b>のが肝。あとから「その要素の文字は？」と聞き返す道が無いし、
/// 聞き返せたとしても<b>押した時点と食い違う</b>。材料が揃っていれば、項目を決める側は
/// 画面を一切知らずに済み、<b>単体試験が窓なしで書ける</b>。
/// </para>
/// </summary>
public sealed record ContextMenuTarget
{
    /// <summary><c>data-context</c> の値。何の上で押されたか。</summary>
    public required string Key { get; init; }

    /// <summary>
    /// その場所が持ち出した材料（行の本文など）。中身の取り決めは <see cref="Key"/> ごとに自由。
    /// </summary>
    public IReadOnlyDictionary<string, string> Data { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>入力できる欄の上か。</summary>
    public bool Editable { get; init; }

    /// <summary>
    /// 伏せて打たせる欄（<c>type=password</c>）か。
    /// <b>伏せ欄ではコピーと切り取りを出さない</b> — 出せば画面に出さない意味が無くなる。
    /// </summary>
    public bool Secret { get; init; }

    /// <summary>
    /// 選択中の文字。
    /// <para>
    /// <b>⚠ ページ側は <c>window.getSelection()</c> だけを見ないこと。</b>
    /// <c>&lt;input&gt;</c> の中の選択はそこに出ないので、入力欄では
    /// <c>selectionStart</c> / <c>selectionEnd</c> から取る必要がある。
    /// </para>
    /// </summary>
    public string SelectionText { get; init; } = string.Empty;

    public string? Get(string name) => Data.TryGetValue(name, out var value) ? value : null;

    /// <summary>空でない材料があるか。項目を出すかどうかの判断に使う。</summary>
    public bool Has(string name) => !string.IsNullOrEmpty(Get(name));
}
