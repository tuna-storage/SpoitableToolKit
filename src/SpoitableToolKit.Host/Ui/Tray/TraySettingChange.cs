namespace SpoitableToolKit.Host.Ui.Tray;

/// <summary>トレイのメニューのチェックで変える設定 1 つ（<see cref="TrayMenu.SettingFor"/>）。</summary>
/// <param name="Key">本体の設定の保存名（<see cref="Settings.HostConfig"/> のフィールドの名前）。</param>
internal sealed record TraySettingChange(string Key, bool Value);
