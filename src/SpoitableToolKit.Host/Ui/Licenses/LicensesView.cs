namespace SpoitableToolKit.Host.Ui.Licenses;

/// <summary>読めなかったファイルは null（画面は「見つかりません」と出す）。</summary>
internal sealed record LicensesView(string? App, string? ThirdParty);
