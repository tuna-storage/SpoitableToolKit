using SpoitableToolKit.Abstractions.Shortcuts;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Host.Modules;
using SpoitableToolKit.Host.Shortcuts;
using Xunit;

namespace SpoitableToolKit.Tests.Shortcuts;

/// <summary>
/// <c>host.Shortcuts</c>（<see cref="ModuleShortcuts"/>）の決まり。外れたら、何を直せばよいかの文で断る（モジュールを書くのは AI で、黙ると「なぜか出ない」だけが残る）。
/// OnInit の後に断るのをローダーが締めているかは <c>ModuleLoaderTests</c> で見る。
/// </summary>
public sealed class ModuleShortcutsTests
{
    private readonly RegistrationWindow _window = new("sample");

    private ModuleShortcuts Open()
    {
        _window.Open();
        return new ModuleShortcuts("sample", _window);
    }

    private sealed class Tile(string id, string name = "a tile", string? page = null, PanelView? marked = null) : IClickableWristShortcut
    {
        public int Reads { get; private set; }

        public PanelView? MarkedPanel => marked;

        public string Id
        {
            get
            {
                Reads++;
                return id;
            }
        }

        public string Name => name;

        public string? Page => page;

        public ModuleStatus? Status { get; } = new();

        public Task OnTriggerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>登録した順に、登録したときに読んだ値で覚える（プロパティを読むのは 1 回だけ）。</summary>
    [Fact]
    public void WhatIsRegisteredIsReadOnceAndKeptInOrder()
    {
        var shortcuts = Open();
        var first = new Tile("first", "sample.shortcut.first", "tile.html");
        var second = new Tile("second");

        shortcuts.Register(first);
        shortcuts.Register(second);

        var registered = shortcuts.Registered;
        Assert.Equal(["first", "second"], registered.Select(r => r.Id));
        Assert.Equal(("sample.shortcut.first", "tile.html"), (registered[0].Name, registered[0].Page));
        Assert.Same(first, registered[0].Handler);
        Assert.Same(first.Status, registered[0].Status);
        Assert.Equal(1, first.Reads);
    }

    /// <summary>OnInit の外（窓が締まっている）は断る。<c>host.Ui</c> と同じ窓を分け合う。</summary>
    [Fact]
    public void RegisteringOutsideOnInitIsRefused()
    {
        var shortcuts = new ModuleShortcuts("sample", _window);

        var error = Assert.Throws<InvalidOperationException>(() => shortcuts.Register(new Tile("late")));
        Assert.Equal(
            "sample called host.Shortcuts.Register outside OnInit. Register the wrist shortcuts in OnInit: " +
            "the app decides what to show for a module right after OnInit returns, so a later registration would never be shown.",
            error.Message);
        Assert.Empty(shortcuts.Registered);
    }

    /// <summary>Id は英小文字・数字・<c>-</c> だけ（設定に <c>&lt;モジュール&gt;/&lt;Id&gt;</c> で残るので、<c>/</c> が混ざると引き違える）。</summary>
    [Theory]
    [InlineData("Upper")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("-lead")]
    [InlineData("has space")]
    [InlineData("line\n")]
    public void AnIdWithStrayCharactersIsRefused(string id)
    {
        var shortcuts = Open();

        var error = Assert.Throws<ArgumentException>(() => shortcuts.Register(new Tile(id)));
        Assert.StartsWith($"sample called host.Shortcuts.Register with the Id \"{id}\" (Tile). Use 1-64 lower-case letters, digits and '-'", error.Message);
        Assert.Empty(shortcuts.Registered);
    }

    [Fact]
    public void TheSameIdTwiceIsRefusedAndTheFirstIsKept()
    {
        var shortcuts = Open();
        var first = new Tile("go");
        shortcuts.Register(first);

        var error = Assert.Throws<InvalidOperationException>(() => shortcuts.Register(new Tile("go")));
        Assert.Equal("sample called host.Shortcuts.Register twice with the Id \"go\". Each shortcut of a module needs its own Id.", error.Message);
        Assert.Same(first, Assert.Single(shortcuts.Registered).Handler);
    }

    [Fact]
    public void AnEmptyNameIsRefused()
    {
        var shortcuts = Open();

        var error = Assert.Throws<ArgumentException>(() => shortcuts.Register(new Tile("go", " ")));
        Assert.StartsWith("sample called host.Shortcuts.Register for \"go\" with an empty Name.", error.Message);
    }

    /// <summary>ページの字はパネルと同じ決まり（<c>web/</c> の下の <c>.html</c>）。</summary>
    [Theory]
    [InlineData("../other/tile.html")]
    [InlineData("tile.html?x=1")]
    [InlineData("tile.htm")]
    [InlineData("tiles\\tile.html")]
    public void APageThatIsNotAnHtmlFileUnderWebIsRefused(string page)
    {
        var shortcuts = Open();

        var error = Assert.Throws<ArgumentException>(() => shortcuts.Register(new Tile("go", page: page)));
        Assert.StartsWith($"sample called host.Shortcuts.Register for \"go\" with the page \"{page}\".", error.Message);
        Assert.Empty(shortcuts.Registered);
    }

    /// <summary>
    /// 枠を付ける画面（<see cref="IWristShortcut.MarkedPanel"/>）は、<c>ToggleVrPanel</c> に渡すのと同じ名前（<c>&lt;Id&gt;</c> か <c>&lt;Id&gt;#&lt;場所&gt;</c>）で覚える。
    /// </summary>
    [Fact]
    public void AMarkedPanelBecomesTheViewTheWindowShows()
    {
        var shortcuts = Open();

        shortcuts.Register(new Tile("top", marked: new PanelView()));
        shortcuts.Register(new Tile("stats", marked: new PanelView { Place = "stats_7d" }));
        shortcuts.Register(new Tile("plain"));

        Assert.Equal(["sample", "sample#stats_7d", null], shortcuts.Registered.Select(r => r.Marked));
    }

    /// <summary>場所の字は <c>ToggleVrPanel</c> と同じ決まり。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("x#y")]
    [InlineData("../up")]
    public void AMarkedPanelWithStrayCharactersIsRefused(string place)
    {
        var shortcuts = Open();

        var error = Assert.Throws<ArgumentException>(() => shortcuts.Register(new Tile("go", marked: new PanelView { Place = place })));

        Assert.StartsWith($"sample called host.Shortcuts.Register for \"go\" with MarkedPanel = new PanelView {{ Place = \"{place}\" }}. ", error.Message);
        Assert.Empty(shortcuts.Registered);
    }

    [Fact]
    public void ADraggableShortcutIsRegisteredAlongsideClickableOnes()
    {
        var shortcuts = Open();
        var slider = new Slider("volume");

        shortcuts.Register(new Tile("first"));
        shortcuts.Register(slider);

        Assert.Equal(["first", "volume"], shortcuts.Registered.Select(r => r.Id));
        Assert.Same(slider, shortcuts.Registered[1].Handler);

        var error = Assert.Throws<ArgumentException>(() => shortcuts.Register(new Slider("Bad")));
        Assert.StartsWith("sample called host.Shortcuts.Register with the Id \"Bad\" (Slider).", error.Message);
    }

    /// <summary>
    /// 押しとドラッグのどちらで受けるかが決まらないので断る。型を絞れば片方の口に渡せてしまうので、build だけでなく登録のときにも見る。
    /// </summary>
    [Fact]
    public void AShortcutThatIsBothClickableAndDraggableIsRefused()
    {
        var shortcuts = Open();
        var both = new Both();

        var asClickable = Assert.Throws<ArgumentException>(() => shortcuts.Register((IClickableWristShortcut)both));
        var asDraggable = Assert.Throws<ArgumentException>(() => shortcuts.Register((IDraggableWristShortcut)both));

        Assert.StartsWith(
            "sample called host.Shortcuts.Register with Both, which implements both IClickableWristShortcut and IDraggableWristShortcut. A wrist tile is either pressed or dragged:",
            asClickable.Message);
        Assert.Equal(asClickable.Message, asDraggable.Message);
        Assert.Empty(shortcuts.Registered);
    }

    private sealed class Slider(string id) : IDraggableWristShortcut
    {
        public string Id => id;

        public string Name => "a slider";

        public Task OnStartDragAsync(ShortcutDragEventParams e, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnDraggingAsync(ShortcutDragEventParams e, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnEndDragAsync(ShortcutDragEventParams e, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Both : IClickableWristShortcut, IDraggableWristShortcut
    {
        public string Id => "both";

        public string Name => "both";

        public Task OnTriggerAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnStartDragAsync(ShortcutDragEventParams e, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnDraggingAsync(ShortcutDragEventParams e, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnEndDragAsync(ShortcutDragEventParams e, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
