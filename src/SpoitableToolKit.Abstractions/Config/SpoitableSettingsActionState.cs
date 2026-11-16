namespace SpoitableToolKit.Abstractions.Config;

/// <summary>
/// 動きの行（<see cref="SpoitableSettingsAction"/>）のいまの姿。<b>6 つの欄は全部書く</b>（出さないものも <c>= null</c> と書く）。
/// <code>
/// new SpoitableSettingsActionState
/// {
///     Tone = SpoitableSettingsTone.Warn,
///     Status = "見当たりません",
///     Detail = "TCP 3476 を許可するファイアウォールのルールが見つかりません。",
///     Button = "ファイアウォールに追加",       // null = ボタンを出さない
///     ButtonNote = "管理者の確認が表示されます",
///     Copy = "New-NetFirewallRule …",         // null = コピーの欄を出さない
/// }
/// </code>
/// <para>字は画面にそのまま出る（HTML としては読まない）。</para>
/// </summary>
public sealed record SpoitableSettingsActionState
{
    /// <summary>札の色。</summary>
    public required SpoitableSettingsTone Tone { get; init; }

    /// <summary>札の字。短く（「見当たりません」）。空にしない — 空ならホストが「状態の取得に失敗しました」に置き換える。</summary>
    public required string Status { get; init; }

    /// <summary>札の下の説明。出さないなら <c>null</c>。</summary>
    public required string? Detail { get; init; }

    /// <summary>ボタンの字。押すと <see cref="SpoitableSettingsAction.Bind"/> の <c>run</c> が走る。出さないなら <c>null</c>。</summary>
    public required string? Button { get; init; }

    /// <summary>ボタンの横の注記（「管理者の確認が表示されます」）。ボタンが無いときは出ない。出さないなら <c>null</c>。</summary>
    public required string? ButtonNote { get; init; }

    /// <summary>コピーできる 1 行（手で打つ人のためのコマンドなど）。コピーのボタンと一緒に出る。出さないなら <c>null</c>。</summary>
    public required string? Copy { get; init; }
}
