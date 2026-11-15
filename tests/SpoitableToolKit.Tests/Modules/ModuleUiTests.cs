using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Host.Modules;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.Modules;

/// <summary>
/// 外れたら、何が足りないかの文で断る — モジュールを書くのは AI なので、黙って無視すると「なぜか出ない」だけが残る。
/// OnInit の後に断るのをローダーが締めているかは <c>ModuleLoaderTests</c> で見る。
/// </summary>
public sealed class ModuleUiTests
{
    private readonly FakePanelChannel _sink = new();

    private ModuleUi Open()
    {
        var ui = new ModuleUi("sample", _sink);
        ui.OpenRegistration();
        return ui;
    }

    [Fact]
    public void WhatIsRegisteredDuringOnInitIsKept()
    {
        var ui = Open();
        var endpoint = new Endpoint();
        var menu = new Menu();
        var status = new ModuleStatus { Text = "ok" };

        ui.RegisterPanel("index.html");
        ui.RegisterVrPanel("vr/index.html");
        ui.RegisterEndpoint(endpoint);
        ui.RegisterContextMenu(menu);
        ui.RegisterStatus(status);
        ui.CloseRegistration();

        Assert.Equal("index.html", ui.Panel);
        Assert.Equal("vr/index.html", ui.VrPanel);
        Assert.Same(endpoint, ui.Endpoint);
        Assert.Same(menu, ui.ContextMenu);
        Assert.Same(status, ui.Status);
    }

    [Fact]
    public void RegisteringOutsideOnInitIsRefused()
    {
        var ui = new ModuleUi("sample", _sink);

        var before = Assert.Throws<InvalidOperationException>(() => ui.RegisterPanel("index.html"));
        Assert.Equal(
            "sample called host.Ui.RegisterPanel outside OnInit. Register the panel, the endpoint, the context menu and the status " +
            "in OnInit: the app decides what to show for a module right after OnInit returns, so a later registration would never be shown.",
            before.Message);

        ui.OpenRegistration();
        ui.CloseRegistration();

        Assert.Throws<InvalidOperationException>(() => ui.RegisterEndpoint(new Endpoint()));
        Assert.Throws<InvalidOperationException>(() => ui.RegisterContextMenu(new Menu()));
        Assert.Throws<InvalidOperationException>(() => ui.RegisterStatus(new ModuleStatus()));
        Assert.Null(ui.Panel);
        Assert.Null(ui.Endpoint);
    }

    [Fact]
    public void EachThingIsRegisteredOnce()
    {
        var ui = Open();
        var first = new Endpoint();
        ui.RegisterEndpoint(first);
        ui.RegisterPanel("index.html");

        var twice = Assert.Throws<InvalidOperationException>(() => ui.RegisterEndpoint(new Endpoint()));
        Assert.Equal(
            "sample called host.Ui.RegisterEndpoint twice. A module has one of each: register it once in OnInit " +
            "(to answer from several classes, register one that hands each call to the right class).",
            twice.Message);
        Assert.Throws<InvalidOperationException>(() => ui.RegisterPanel("other.html"));

        Assert.Same(first, ui.Endpoint);
        Assert.Equal("index.html", ui.Panel);
    }

    /// <summary>VR のページはパネルの代わりなので、パネルが先。</summary>
    [Fact]
    public void TheVrPageNeedsThePanelFirst()
    {
        var ui = Open();

        var error = Assert.Throws<InvalidOperationException>(() => ui.RegisterVrPanel("vr.html"));
        Assert.StartsWith("sample called host.Ui.RegisterVrPanel(\"vr.html\") before RegisterPanel.", error.Message);
        Assert.Null(ui.VrPanel);

        ui.RegisterPanel("index.html");
        ui.RegisterVrPanel("vr.html");
        Assert.Equal("vr.html", ui.VrPanel);
    }

    /// <summary>
    /// ページの字は <c>web/</c> の下の <c>.html</c> だけ（画面がこの字で <c>spoitable://&lt;Id&gt;/…</c> を組む）。断ったものは覚えない。
    /// </summary>
    [Theory]
    [InlineData("../index.html")]
    [InlineData("index.htm")]
    [InlineData("index.html?x=1")]
    [InlineData("web\\index.html")]
    [InlineData("/index.html")]
    [InlineData("")]
    public void APageThatIsNotAnHtmlFileUnderWebIsRefused(string entry)
    {
        var ui = Open();

        var error = Assert.Throws<ArgumentException>(() => ui.RegisterPanel(entry));
        Assert.StartsWith($"sample called host.Ui.RegisterPanel(\"{entry}\"). Give a page under the module's web/ folder, ending in .html", error.Message);
        Assert.Null(ui.Panel);
    }

    /// <summary>流す・開かせるは縛らない（OnInit の後も、どのスレッドからでも）。</summary>
    [Fact]
    public void PushAndShowWorkAfterOnInit()
    {
        var ui = Open();
        ui.CloseRegistration();

        ui.Push("status", """{"bpm":72}""");
        ui.Show();

        Assert.Equal([("status", """{"bpm":72}""")], _sink.Pushed);
        Assert.Equal(1, _sink.Shown);
    }

    /// <summary>
    /// <c>ToggleVrPanel</c> は空間の窓へ <c>&lt;Id&gt;</c>（場所があれば <c>&lt;Id&gt;#&lt;場所&gt;</c>）で頼む。
    /// <b>パネルを出すと決まったあと（OnInit の後、ファイルもあった）だけ</b> — 登録の字だけで頼むと、中が 404 の窓が開く。
    /// </summary>
    [Fact]
    public void TogglingTheVrPanelAsksTheWindowOnlyOnceThePanelIsShown()
    {
        var ui = Open();
        ui.RegisterPanel("index.html");
        ui.CloseRegistration();

        ui.ToggleVrPanel();
        Assert.Empty(_sink.Toggled);

        ui.PanelDecided(true);
        ui.ToggleVrPanel();
        ui.ToggleVrPanel("stats_7d");

        Assert.Equal(["sample", "sample#stats_7d"], _sink.Toggled);
    }

    /// <summary>場所は URL の <c>#</c> の後ろに入るので、英数字・<c>-</c>・<c>_</c> だけ（外れたら頼まずに断る）。</summary>
    [Theory]
    [InlineData("a b")]
    [InlineData("x#y")]
    [InlineData("")]
    [InlineData("../x")]
    public void APlaceWithStrayCharactersIsRefused(string place)
    {
        var ui = Open();
        ui.PanelDecided(true);

        var error = Assert.Throws<ArgumentException>(() => ui.ToggleVrPanel(place));
        Assert.StartsWith($"sample called host.Ui.ToggleVrPanel(\"{place}\"). The place goes into the panel's URL after '#'", error.Message);
        Assert.Empty(_sink.Toggled);
    }

    private sealed class Endpoint : IUiEndpoint
    {
        public Task<string> HandleUiCallAsync(string method, string payload, CancellationToken cancellationToken = default) =>
            Task.FromResult("{}");
    }

    private sealed class Menu : IContextMenuSource
    {
        public IReadOnlyList<ContextMenuItem> GetContextMenu(ContextMenuTarget target) => [];

        public Task InvokeContextMenuAsync(string id, ContextMenuTarget target, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
