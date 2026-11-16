namespace SpoitableToolKit.Abstractions.Config;

/// <summary>
/// 動きの行（<see cref="SpoitableSettingsAction"/>）のいまの姿。<b>欄は全部書く</b>（出さないものは <c>null</c>）。
/// <code>
/// new SpoitableSettingsActionState(
///     Tone: SpoitableSettingsTone.Warn,
///     Status: "見当たりません",
///     Detail: "TCP 3476 を許可するファイアウォールのルールが見つかりません。",
///     Button: "ファイアウォールに追加",       // null = ボタンを出さない
///     ButtonNote: "管理者の確認が表示されます",
///     Copy: "New-NetFirewallRule …");         // null = コピーの欄を出さない
/// </code>
/// <para>字は画面にそのまま出る（HTML としては読まない）。</para>
/// </summary>
/// <param name="Tone">札の色。</param>
/// <param name="Status">札の字。短く（「見当たりません」）。空にしない — 空ならホストが「状態の取得に失敗しました」に置き換える。</param>
/// <param name="Detail">札の下の説明。出さないなら <c>null</c>。</param>
/// <param name="Button">ボタンの字。押すと <see cref="SpoitableSettingsAction.Bind"/> の <c>run</c> が走る。出さないなら <c>null</c>。</param>
/// <param name="ButtonNote">ボタンの横の注記（「管理者の確認が表示されます」）。ボタンが無いときは出ない。出さないなら <c>null</c>。</param>
/// <param name="Copy">コピーできる 1 行（手で打つ人のためのコマンドなど）。コピーのボタンと一緒に出る。出さないなら <c>null</c>。</param>
public sealed record SpoitableSettingsActionState(
    SpoitableSettingsTone Tone,
    string Status,
    string? Detail,
    string? Button,
    string? ButtonNote,
    string? Copy);
