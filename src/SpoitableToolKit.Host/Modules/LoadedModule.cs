using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Shortcuts;
using SpoitableToolKit.Host.Modules.Loading.Models;
using SpoitableToolKit.Host.Shortcuts;

namespace SpoitableToolKit.Host.Modules;

internal sealed class LoadedModule
{
    public required ModuleCandidate Candidate { get; init; }
    public required ISpoitableModule Instance { get; init; }
    public required IModuleHost Host { get; init; }

    /// <summary>
    /// モジュールが OnInit で <c>host.Ui</c> に登録したもの（パネル・呼ばれる口・右クリック・様子）。
    /// <b>読む側はここだけを見る</b>（モジュールのクラスが interface を実装しているかは見ない）。
    /// </summary>
    public required ModuleUi Ui { get; init; }

    public required ModuleShortcuts Shortcuts { get; init; }

    public required string WebRoot { get; init; }

    /// <summary>
    /// パネルの入口。<b>OnInit の後に決める</b>（登録が無い・実体が無いなら null で、サイドバーに出さない）。
    /// </summary>
    public string? PanelEntry { get; set; }

    /// <summary>
    /// VR の空間の窓で出すページ（<see cref="ModuleUi.VrPanel"/>）。<b>登録が無い・実体が無い・<see cref="PanelEntry"/> が無い</b>なら null
    /// （空間の窓でも <see cref="PanelEntry"/> を出す）。OnInit の後に決める。
    /// </summary>
    public string? VrPanelEntry { get; set; }

    /// <summary>
    /// 手首のタイルに描くページを登録していて、ホストがそれを受けた（<see cref="IWristShortcut.Page"/>）。
    /// <b>パネルが無くても <c>spoitable://&lt;Id&gt;/</c> を配る</b>（<see cref="ModuleManager.ResolveWebRoot"/>）。読み込みの後に 1 回だけ立てる。
    /// </summary>
    public bool ServesTilePages { get; set; }

    public ModuleState State { get; set; } = ModuleState.Initializing;

    public string Phase { get; set; } = "-";

    public string? Failure { get; set; }
    public string Id => Instance.Id;
}
