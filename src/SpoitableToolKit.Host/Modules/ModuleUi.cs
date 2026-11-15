using System.Text.RegularExpressions;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Host.Ui.Push;

namespace SpoitableToolKit.Host.Modules;

/// <summary>
/// モジュール 1 つ分の <see cref="IModuleUi"/>（<c>host.Ui</c>）。<b>登録を覚えるだけ</b>で、何を出すかは読み込みの側
/// （<see cref="ModuleManager"/>・<c>Program</c>）が OnInit の後にここを読んで決める。
/// <para>
/// 見るのは 4 つ: 登録できるのは OnInit の間（<see cref="RegistrationWindow"/>）だけ・それぞれ 1 回だけ・VR のページはパネルの後・ページの字。
/// ファイルがあるかは見ない（<see cref="ModulePanelEntries"/> が見る）。
/// <see cref="Push"/>・<see cref="Show"/>・<see cref="ToggleVrPanel"/> は縛らない（OnInit の中から別のスレッドで流れるものもある）。
/// </para>
/// </summary>
/// <param name="moduleId">文に出す名前。</param>
/// <param name="sink">ページへ流す先。</param>
/// <param name="window">登録を受ける間。<c>host.Shortcuts</c> と分け合う。null なら自分だけのものを持つ。</param>
internal sealed partial class ModuleUi(string moduleId, IPanelSink sink, RegistrationWindow? window = null) : IModuleUi
{
    private const string WhatToRegister = "the panel, the endpoint, the context menu and the status";

    private readonly RegistrationWindow _window = window ?? new RegistrationWindow(moduleId);

    private volatile bool _panelShown;

    public string? Panel { get; private set; }

    public string? VrPanel { get; private set; }

    public IUiEndpoint? Endpoint { get; private set; }

    public IContextMenuSource? ContextMenu { get; private set; }

    public ModuleStatus? Status { get; private set; }

    public void OpenRegistration() => _window.Open();

    /// <summary>登録を締める（OnInit が返った直後。投げても false でも）。このあとの登録は断る。</summary>
    public void CloseRegistration() => _window.Close();

    /// <summary>
    /// OnInit の後に決めた「パネルを出すか」（登録があって、ファイルもあった）。<see cref="ToggleVrPanel"/> はこれが立っているときだけ窓へ頼む
    /// （登録の字だけで頼むと、中身が 404 の窓が開く）。
    /// </summary>
    public void PanelDecided(bool shown) => _panelShown = shown;

    public void RegisterPanel(string entry)
    {
        CheckPage(nameof(RegisterPanel), entry);

        lock (_window.Gate)
        {
            Admit(nameof(RegisterPanel), Panel);
            Panel = entry;
        }
    }

    public void RegisterVrPanel(string entry)
    {
        CheckPage(nameof(RegisterVrPanel), entry);

        lock (_window.Gate)
        {
            Admit(nameof(RegisterVrPanel), VrPanel);

            if (Panel is null)
                throw new InvalidOperationException(
                    $"{moduleId} called host.Ui.RegisterVrPanel(\"{entry}\") before RegisterPanel. The VR page replaces the panel in the VR window, " +
                    "so the module needs a panel first: call host.Ui.RegisterPanel(\"index.html\") (or its own page) before RegisterVrPanel.");

            VrPanel = entry;
        }
    }

    public void RegisterEndpoint(IUiEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        lock (_window.Gate)
        {
            Admit(nameof(RegisterEndpoint), Endpoint);
            Endpoint = endpoint;
        }
    }

    public void RegisterContextMenu(IContextMenuSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_window.Gate)
        {
            Admit(nameof(RegisterContextMenu), ContextMenu);
            ContextMenu = source;
        }
    }

    public void RegisterStatus(ModuleStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        lock (_window.Gate)
        {
            Admit(nameof(RegisterStatus), Status);
            Status = status;
        }
    }

    public void Push(string channel, string payloadJson) => sink.Push(channel, payloadJson);

    public void Show() => sink.Show();

    public void ToggleVrPanel(string? place = null)
    {
        if (place is not null && !Place().IsMatch(place))
            throw new ArgumentException(
                $"{moduleId} called host.Ui.ToggleVrPanel(\"{place}\"). The place goes into the panel's URL after '#', " +
                "so use 1-64 letters, digits, '-' and '_' only (for example \"stats_7d\"), or no place to open the panel as it is.", nameof(place));

        if (!_panelShown) return;

        sink.ToggleVrPanel(ViewOf(moduleId, place));
    }

    private void Admit(string method, object? already)
    {
        _window.Admit($"host.Ui.{method}", WhatToRegister);

        if (already is not null)
            throw new InvalidOperationException(
                $"{moduleId} called host.Ui.{method} twice. A module has one of each: register it once in OnInit " +
                "(to answer from several classes, register one that hands each call to the right class).");
    }

    /// <summary>ページの字を検める（画面がこの字で <c>spoitable://&lt;Id&gt;/&lt;ページ&gt;</c> を組むので、<c>?</c>・<c>#</c>・<c>..</c> を入れさせない）。</summary>
    private void CheckPage(string method, string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!ModulePanelEntries.IsPagePath(entry))
            throw new ArgumentException(
                $"{moduleId} called host.Ui.{method}(\"{entry}\"). Give a page under the module's web/ folder, ending in .html, " +
                "with letters, digits, '-', '_' and '/' only (for example \"index.html\" or \"vr/index.html\").", nameof(entry));
    }

    /// <summary>パネルの中の場所の字か（<see cref="ToggleVrPanel"/> と、手首のタイルが名乗る枠 <c>IWristShortcut.MarkedPanel</c> で共通）。</summary>
    internal static bool IsPlace(string place) => Place().IsMatch(place);

    /// <summary>空間の窓へ頼む画面の名前（<c>&lt;Id&gt;</c> か <c>&lt;Id&gt;#&lt;場所&gt;</c>）。手首の枠もこの名前で比べる。</summary>
    internal static string ViewOf(string moduleId, string? place) => place is null ? moduleId : $"{moduleId}#{place}";

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}\\z")]
    private static partial Regex Place();
}
