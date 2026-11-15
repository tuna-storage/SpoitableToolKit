using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions.Clipboard;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Host.Ui.ContextMenu;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.ContextMenu;

public class ContextMenuTests
{
    private static ContextMenus Menus(IClipboard clipboard, ContextMenus.SourceLookup? sources = null) =>
        new(sources ?? (_ => null), clipboard, NullLogger.Instance);

    private static ContextMenuTarget Target(
        string key = "", bool editable = false, bool secret = false, string selection = "",
        Dictionary<string, string>? data = null) =>
        new()
        {
            Key = key,
            Editable = editable,
            Secret = secret,
            SelectionText = selection,
            Data = data ?? [],
        };

    private static string[] Ids(IEnumerable<ContextMenuEntry> items) =>
        [.. items.Select(i => i.Separator ? "—" : i.Id)];

    /// <summary>何も無い背景では 1 つも出さない（画面はこれを見てメニューごと開かない。空の箱は「壊れている」ようにしか見えない）。</summary>
    [Fact]
    public void NothingIsOfferedOnBarePage()
    {
        Assert.Empty(Menus(new FakeClipboard()).Build(Target(), owner: null));
    }

    [Fact]
    public void SelectedTextCanBeCopied()
    {
        var items = Menus(new FakeClipboard()).Build(Target(selection: "hello"), owner: null);

        Assert.Equal(["host copy"], Ids(items));
        Assert.Equal(CommonPhrases.Ui.Copy, Assert.Single(items).LabelKey);
    }

    /// <summary>入力欄では選んでいなくても切り取りとコピーを出す（灰色）。ここにその操作があることは見せたい。</summary>
    [Fact]
    public void AnInputOffersTheEditingItems()
    {
        var items = Menus(new FakeClipboard()).Build(Target(editable: true), owner: null);

        Assert.Equal(["host cut", "host copy", "host paste"], Ids(items));
        Assert.Equal([false, false, true], items.Select(i => i.Enabled));

        Assert.Equal([CommonPhrases.Ui.Cut, CommonPhrases.Ui.Copy, CommonPhrases.Ui.Paste], items.Select(i => i.LabelKey));
    }

    [Fact]
    public void SelectingInAnInputEnablesCutAndCopy()
    {
        var items = Menus(new FakeClipboard()).Build(Target(editable: true, selection: "abc"), owner: null);

        Assert.Equal([true, true, true], items.Select(i => i.Enabled));
    }

    /// <summary>伏せ欄からは持ち出せない（出せば「画面に出さない」ことの意味が無くなる）。入れる側（貼り付け）は守る対象ではないので残す。</summary>
    [Fact]
    public void ASecretFieldOffersOnlyPaste()
    {
        var items = Menus(new FakeClipboard()).Build(Target(editable: true, secret: true, selection: "token"), owner: null);

        Assert.Equal(["host paste"], Ids(items));
    }

    [Fact]
    public void ALogRowCanBeCopied()
    {
        var items = Menus(new FakeClipboard())
            .Build(Target("log-row", data: new() { ["text"] = "12:00 PlayerJoined foo" }), owner: null);

        Assert.Equal(["host copyRow"], Ids(items));
    }

    /// <summary>明細は開いている行にしか無い。畳んだ行に押せない項目を並べない。</summary>
    [Fact]
    public void TheDetailItemAppearsOnlyWhenTheRowIsOpen()
    {
        var closed = Menus(new FakeClipboard())
            .Build(Target("log-row", data: new() { ["text"] = "row" }), owner: null);

        var open = Menus(new FakeClipboard())
            .Build(Target("log-row", data: new() { ["text"] = "row", ["detail"] = "…" }), owner: null);

        Assert.Equal(["host copyRow"], Ids(closed));
        Assert.Equal(["host copyRow", "host copyDetail"], Ids(open));
    }

    [Fact]
    public void AnUnknownPlaceHasNoItemsOfItsOwn()
    {
        var items = Menus(new FakeClipboard())
            .Build(Target("something-else", data: new() { ["text"] = "row" }), owner: null);

        Assert.Empty(items);
    }

    [Fact]
    public async Task CopyingWritesTheSelection()
    {
        var clipboard = new FakeClipboard();

        var result = await Menus(clipboard).InvokeAsync("host copy", Target(selection: "hello"), owner: null);

        Assert.True(result.Ok);
        Assert.Null(result.Act);
        Assert.Equal("hello", clipboard.Text);
    }

    [Fact]
    public async Task CopyingARowWritesWhatTheRowCarried()
    {
        var clipboard = new FakeClipboard();

        await Menus(clipboard).InvokeAsync(
            "host copyRow", Target("log-row", data: new() { ["text"] = "12:00 PlayerJoined foo" }), owner: null);

        Assert.Equal("12:00 PlayerJoined foo", clipboard.Text);
    }

    /// <summary>切り取りは「書いてから消す」。指示だけ返り、消すのは画面。</summary>
    [Fact]
    public async Task CuttingWritesTheSelectionAndAsksForItToBeRemoved()
    {
        var clipboard = new FakeClipboard();

        var result = await Menus(clipboard).InvokeAsync("host cut", Target(editable: true, selection: "abc"), owner: null);

        Assert.Equal("abc", clipboard.Text);
        Assert.Equal("replaceSelection", result.Act);
        Assert.Equal(string.Empty, result.Text);
    }

    /// <summary>書けなかったら消さない（先に消してから書けなかったら、選んでいた文字がどこにも残らない）。</summary>
    [Fact]
    public async Task NothingIsRemovedWhenTheClipboardCannotBeWritten()
    {
        var clipboard = new FakeClipboard { Writable = false };

        var result = await Menus(clipboard).InvokeAsync("host cut", Target(editable: true, selection: "abc"), owner: null);

        Assert.False(result.Ok);
        Assert.Null(result.Act);
    }

    /// <summary>貼り付けの中身はホストが読んで返す。ページにクリップボードを読む口は開けていない。</summary>
    [Fact]
    public async Task PastingBringsTheClipboardBackToThePage()
    {
        var clipboard = new FakeClipboard { Text = "from elsewhere" };

        var result = await Menus(clipboard).InvokeAsync("host paste", Target(editable: true), owner: null);

        Assert.Equal("replaceSelection", result.Act);
        Assert.Equal("from elsewhere", result.Text);
    }

    /// <summary>空で上書きすると、選んでいた文字がただ消える。空文字も通す（<c>null</c> だけで見ていると、長さを見ない実装でも緑になる）。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task PastingAnEmptyClipboardChangesNothing(string? held)
    {
        var result = await Menus(new FakeClipboard { Text = held })
            .InvokeAsync("host paste", Target(editable: true, selection: "abc"), owner: null);

        Assert.True(result.Ok);
        Assert.Null(result.Act);
    }

    [Fact]
    public void AModuleCanAddItsOwnItems()
    {
        var items = Menus(new FakeClipboard(), _ => new FakeMenuSource())
            .Build(Target("player"), owner: "hrs");

        Assert.Equal(["hrs open"], Ids(items));
    }

    /// <summary>持ち主は id の頭に付く。モジュールが <c>copy</c> という印を返しても、ホストの <c>copy</c> とは別物として戻ってくる。</summary>
    [Fact]
    public void AModuleItemCannotCollideWithAHostItem()
    {
        var items = Menus(new FakeClipboard(), _ => new FakeMenuSource { ItemId = "copy" })
            .Build(Target(selection: "hello"), owner: "hrs");

        Assert.Equal(["host copy", "—", "hrs copy"], Ids(items));
    }

    [Fact]
    public void NoDividerIsDrawnWhenOnlyTheModuleHasItems()
    {
        var items = Menus(new FakeClipboard(), _ => new FakeMenuSource())
            .Build(Target(), owner: "hrs");

        Assert.Equal(["hrs open"], Ids(items));
    }

    [Fact]
    public void TheShellDoesNotAskAnyModule()
    {
        var asked = false;

        var items = Menus(new FakeClipboard(), _ =>
        {
            asked = true;
            return new FakeMenuSource();
        }).Build(Target(selection: "hello"), owner: null);

        Assert.False(asked);
        Assert.Equal(["host copy"], Ids(items));
    }

    [Fact]
    public void AThrowingModuleLosesOnlyItsOwnItems()
    {
        var items = Menus(new FakeClipboard(), _ => new ExplodingMenuSource())
            .Build(Target(selection: "hello"), owner: "hrs");

        Assert.Equal(["host copy"], Ids(items));
    }

    [Fact]
    public async Task AThrowingModuleItemIsReportedAsAFailure()
    {
        var result = await Menus(new FakeClipboard(), _ => new ExplodingMenuSource())
            .InvokeAsync("hrs open", Target(), owner: "hrs");

        Assert.False(result.Ok);
    }

    /// <summary>一覧を出したのとは別のモジュールの項目は実行しない（名乗りの持ち主はシェルが origin から決めているので、id の頭だけ差し替えても動かせない）。</summary>
    [Fact]
    public async Task AnItemClaimedForAnotherModuleIsRefused()
    {
        var source = new FakeMenuSource();

        var result = await Menus(new FakeClipboard(), _ => source)
            .InvokeAsync("videourl open", Target(), owner: "hrs");

        Assert.False(result.Ok);
        Assert.Null(source.Invoked);
    }

    [Fact]
    public async Task AModuleItemReachesItsOwner()
    {
        var source = new FakeMenuSource();

        var result = await Menus(new FakeClipboard(), _ => source).InvokeAsync("hrs open", Target(), owner: "hrs");

        Assert.True(result.Ok);
        Assert.Equal("open", source.Invoked);
    }

    /// <summary>
    /// 名乗りはシェルと <c>bridge.js</c> の 2 つにある（別 origin で動く別のページなので共有できない）。
    /// 「選択を持てる型の一覧」は同梱モジュールの画面に数値の欄が無く、パネル側を e2e で壊せないので、両方に同じ一覧があることを直接見る。
    /// </summary>
    [Fact]
    public void BothSidesAgreeOnWhichFieldsHaveASelection()
    {
        const string allowList = "['text', 'search', 'url', 'tel', 'password'].includes(field.type)";

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SpoitableToolKit.slnx"))) root = root.Parent;
        Assert.NotNull(root);

        foreach (var page in new[] { "web/index.html", "web/_host/bridge.js" })
        {
            var text = File.ReadAllText(Path.Combine(root.FullName, "src/SpoitableToolKit.Host", page));

            Assert.True(text.Contains(allowList, StringComparison.Ordinal), $"{page} に一覧が無い");
        }
    }

    private sealed class FakeMenuSource : IContextMenuSource
    {
        public string ItemId { get; init; } = "open";

        public string? Invoked { get; private set; }

        public IReadOnlyList<ContextMenuItem> GetContextMenu(ContextMenuTarget target) =>
            [new ContextMenuItem { Id = ItemId, LabelKey = "hrs.menu.open" }];

        public Task InvokeContextMenuAsync(string id, ContextMenuTarget target, CancellationToken cancellationToken = default)
        {
            Invoked = id;
            return Task.CompletedTask;
        }
    }

    private sealed class ExplodingMenuSource : IContextMenuSource
    {
        public IReadOnlyList<ContextMenuItem> GetContextMenu(ContextMenuTarget target) => throw new InvalidOperationException("boom");

        public Task InvokeContextMenuAsync(string id, ContextMenuTarget target, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");
    }
}
