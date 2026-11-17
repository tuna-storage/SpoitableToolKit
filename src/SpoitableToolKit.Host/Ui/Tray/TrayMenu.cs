using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Vr;

namespace SpoitableToolKit.Host.Ui.Tray;

/// <summary>
/// トレイの右クリックのメニューの中身。<b>開くたびに組み立て直す</b>ので、言語を変えればその場で次に開いたメニューから変わり、
/// チェックもそのときの設定を写す（設定の画面で変えても、メニューは古いままにならない）。
/// 押されたときの動きは持たない（名前を返すだけ。動かすのは <see cref="Desktop.DesktopApp"/>）。設定を変える項目は、何をどう変えるかだけを答える（<see cref="SettingFor"/>）。
/// </summary>
internal static class TrayMenu
{
    public const string Open = "open";

    /// <summary>「VR 機能」のサブメニューの親（押しても何もしない）。</summary>
    public const string Vr = "vr";

    public const string VrOverlay = "vr.overlay";

    public const string VrDashboard = "vr.dashboard";

    public const string Exit = "exit";

    public static IReadOnlyList<TrayMenuItem> Build(ILocalizer text, VrFeatures vr) =>
    [
        new() { Id = Open, Label = text["host.tray.open"], Default = true },
        new()
        {
            Id = Vr,
            Label = text["host.tray.vr"],
            Children =
            [
                new() { Id = VrOverlay, Label = text["host.tray.vrOverlay"], Checked = vr.Overlay },
                new() { Id = VrDashboard, Label = text["host.tray.vrDashboard"], Checked = vr.Dashboard },
            ],
        },
        TrayMenuItem.Line,
        new() { Id = Exit, Label = text["host.tray.exit"] },
    ];

    /// <summary>
    /// 設定を変える項目なら、どの設定（保存名）を何にするか。チェックを押したら入と切を入れ替える（いまの設定から決める。メニューを開いた時点の写しではない）。
    /// 設定を変えない項目なら null。書くのは呼んだ側で、設定の画面と同じ適用の道を通す（<see cref="SettingsApplier.SetHost"/>）。
    /// </summary>
    public static TraySettingChange? SettingFor(string id, HostConfig config) => id switch
    {
        VrOverlay => new TraySettingChange(nameof(HostConfig.VrOverlay), !config.VrOverlay),
        VrDashboard => new TraySettingChange(nameof(HostConfig.VrDashboard), !config.VrDashboard),
        _ => null,
    };
}
