namespace SpoitableToolKit.Host.Updates.Models;

/// <param name="Installed">インストールした版か。false なら確かめない（「更新を確認」は「インストールした版でだけ使えます」）。</param>
/// <param name="Banner">シェルの上にバナーを出すか（スキップした版なら出さない。落としている間は出す）。</param>
/// <param name="HasNotes">変更点が書かれているか（無ければ「変更点」を出さない）。</param>
internal sealed record AppUpdateView(
    bool Installed,
    string CurrentVersion,
    string Phase,
    string? Version,
    bool Downgrade,
    bool Banner,
    int? Percent,
    bool HasNotes);
