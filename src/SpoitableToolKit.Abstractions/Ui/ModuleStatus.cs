using SpoitableToolKit.Abstractions.Config;

namespace SpoitableToolKit.Abstractions.Ui;

/// <summary>
/// モジュールの「いまの様子」を短い文字で渡す口。<see cref="IModuleUi.RegisterStatus"/> で渡す（OnInit の中で 1 回）。
/// <para>
/// 出る場所は 3 つ: <b>サイドバーのそのモジュールの行の右</b>（「● 72 bpm」、悪いときは色の札「失敗 2」）、
/// <b>手首の帯のチップ</b>、<b>ダッシュボードの札</b>（<see cref="Note"/> が 2 行目）。
/// <b>押すとそのモジュールの画面が開く</b>（押したときの動きは選べない）。
/// </para>
/// <para>
/// <b>インスタンスはホストと共有する</b>（<see cref="IModuleConfig"/> と同じ形）。
/// モジュールは持っているこれを書き換えるだけでよく、ホストは参照を借りて読む。
/// </para>
/// <para>
/// <b>渡せるのは文字列と状態だけで、HTML は渡せない。</b> サイドバーはシェルの origin なので、
/// モジュールに HTML を書かせると<b>パネルを別 origin に分けた意味が消える</b>。
/// 色は状態からシェルが決めるため、テーマにも自動で追従する。
/// </para>
/// <para>
/// <b>どのスレッドから書いてもよい。書いても何も呼ばれない</b> — ホストは画面の周期（200 ms ごと）に読みに来て、
/// 前に描いたものと違えば描き直す（ゲームのループが、毎フレーム値を読んで描くのと同じ）。
/// 毎秒同じ値（72 → 72）を書いても、画面には何も送られない。
/// </para>
/// </summary>
public sealed class ModuleStatus
{
    private string _text = "";
    private StatusState _state;
    private string? _note;

    /// <summary>
    /// 短い値（「72 bpm」「失敗 2」）。<c>null</c> を入れると空文字になる。<b>空なら出さない。</b>
    /// <para>
    /// <b>見出しは書かない。</b> 「心拍」はサイドバーの行・札の見出しにもう出ている。
    /// </para>
    /// </summary>
    public string Text
    {
        get => _text;
        set => _text = value ?? "";
    }

    /// <summary>
    /// 色分け。<see cref="StatusState.Off"/> / <see cref="StatusState.Ok"/> は点と薄い字、
    /// <see cref="StatusState.Warn"/> / <see cref="StatusState.Bad"/> は色の札になる。
    /// </summary>
    public StatusState State
    {
        get => _state;
        set => _state = value;
    }

    /// <summary>
    /// ダッシュボードの札の 2 行目（「最小 61 · 最大 118」）。<c>null</c> か空なら出さない。
    /// </summary>
    public string? Note
    {
        get => _note;
        set => _note = string.IsNullOrEmpty(value) ? null : value;
    }
}
