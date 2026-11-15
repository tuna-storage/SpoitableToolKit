using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Status;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Status;

public sealed class StatusBoardTests
{
    private static (StatusBoard Board, HostConfig Config, Recording Sent) New()
    {
        var config = new HostConfig();
        var sent = new Recording();

        return (new StatusBoard(config, sent), config, sent);
    }

    private static ModuleStatus Item(string text, StatusState state = StatusState.Ok, string? note = null) =>
        new() { Text = text, State = state, Note = note };

    [Fact]
    public void ItemsKeepTheOrderTheyWereAddedIn()
    {
        var (board, _, _) = New();

        board.Register("host", Item("ログ 未検出", StatusState.Off));
        board.Register("hrs", Item("72 bpm"));

        Assert.Equal(["host", "hrs"], board.Current().Select(i => i.Id));
        Assert.Equal(["off", "ok"], board.Current().Select(i => i.State));
    }

    /// <summary>重さは 4 つとも画面の <c>data-state</c> にそのまま使える小文字で渡る。</summary>
    [Fact]
    public void EveryStateReachesTheScreenInLowerCase()
    {
        var (board, _, _) = New();

        board.Register("a", Item("a", StatusState.Off));
        board.Register("b", Item("b", StatusState.Ok));
        board.Register("c", Item("c", StatusState.Warn));
        board.Register("d", Item("d", StatusState.Bad));

        Assert.Equal(["off", "ok", "warn", "bad"], board.Current().Select(i => i.State));
    }

    [Fact]
    public void EmptyItemsAreNotShown()
    {
        var (board, _, _) = New();

        var hrs = Item("");
        board.Register("hrs", hrs);
        Assert.Empty(board.Current());

        hrs.Text = "72 bpm";
        Assert.Single(board.Current());
    }

    [Fact]
    public void HiddenItemsAreNotShown()
    {
        var (board, config, _) = New();

        board.Register("host", Item("ログ"));
        board.Register("hrs", Item("72 bpm"));
        config.HiddenSidebarStatus = ["hrs"];

        Assert.Equal(["host"], board.Current().Select(i => i.Id));
    }

    /// <summary>ダッシュボードの札の 2 行目は後から書いても届く（変更で印が付かないと、最小・最大が出ないまま止まる）。</summary>
    [Fact]
    public void TheNoteReachesTheScreenAndALaterNoteToo()
    {
        var (board, _, sent) = New();

        var hrs = Item("72 bpm");
        board.Register("hrs", hrs);
        board.Flush();
        Assert.Null(board.Current().Single().Note);

        hrs.Note = "最小 61 · 最大 118";
        board.Flush();

        Assert.Equal(2, sent.On("statusItems").Length);
        Assert.Equal("最小 61 · 最大 118", board.Current().Single().Note);
    }

    [Fact]
    public void AnEmptyNoteIsNoNote()
    {
        var status = Item("72 bpm", note: "");

        Assert.Null(status.Note);
    }

    [Fact]
    public void NothingIsSentWhenNothingChanged()
    {
        var (board, _, sent) = New();

        var hrs = Item("72 bpm");
        board.Register("hrs", hrs);

        board.Flush();
        board.Flush();
        Assert.Single(sent.On("statusItems"));

        hrs.Text = "72 bpm";
        hrs.Note = null;
        board.Flush();
        Assert.Single(sent.On("statusItems"));

        hrs.Text = "73 bpm";
        board.Flush();
        Assert.Equal(2, sent.On("statusItems").Length);
    }

    /// <summary>周と周の間に 72 → 73 → 72 と変わって戻っても送らない。画面に出るのは周のときの値だけ。</summary>
    [Fact]
    public void AChangeThatGoesBackBeforeTheSweepIsNotSent()
    {
        var (board, _, sent) = New();

        var hrs = Item("72 bpm");
        board.Register("hrs", hrs);
        board.Flush();

        hrs.Text = "73 bpm";
        hrs.Text = "72 bpm";
        board.Flush();

        Assert.Single(sent.On("statusItems"));
    }

    [Fact]
    public void AChangeToAHiddenItemSendsNothing()
    {
        var (board, config, sent) = New();

        var hrs = Item("72 bpm");
        board.Register("host", Item("ログ"));
        board.Register("hrs", hrs);
        config.HiddenSidebarStatus = ["hrs"];

        board.Flush();
        Assert.Single(sent.On("statusItems"));

        hrs.Text = "73 bpm";
        board.Flush();
        Assert.Single(sent.On("statusItems"));
    }

    /// <summary>「サイドバーに出さない状態」を変えたら、値が同じでも次の周で配り直す（並ぶ中身が変わるので）。</summary>
    [Fact]
    public void ChangingTheHiddenListIsSentOnTheNextSweep()
    {
        var (board, config, sent) = New();

        board.Register("hrs", Item("72 bpm"));
        board.Flush();

        config.HiddenSidebarStatus = ["hrs"];
        board.Flush();

        var pushes = sent.On("statusItems");
        Assert.Equal(2, pushes.Length);
        Assert.Empty((StatusBoardItem[])pushes[1].Payload);
    }

    /// <summary>設定の候補には、隠しているものも含めて全部出る（隠したものを戻せるように）。</summary>
    [Fact]
    public void TheNamesIncludeHiddenAndEmptyItems()
    {
        var (board, config, _) = New();

        board.Register("host", Item("ログ"));
        board.Register("hrs", Item(""));
        config.HiddenSidebarStatus = ["host"];

        Assert.Equal(["host", "hrs"], board.Names());
    }

    /// <summary>bind 越しの形にも 2 行目が乗る（record のままでは橋を渡れない）。</summary>
    [Fact]
    public void TheBridgeShapeCarriesTheNote()
    {
        var (board, _, _) = New();

        board.Register("hrs", Item("72 bpm", note: "最小 61"));

        var item = Assert.Single(board.ForBridge());
        Assert.Equal("最小 61", item.GetType().GetProperty("note")!.GetValue(item));
    }

    /// <summary>手首はサイドバーとは別に絞る（サイドバーで隠したものが手首に出ていてよく、逆も同じ）。</summary>
    [Fact]
    public void TheWristAndTheSidebarHideTheirOwnItems()
    {
        var (board, config, _) = New();

        board.Register("host", Item("ログ"));
        board.Register("hrs", Item("72 bpm"));
        config.HiddenSidebarStatus = ["hrs"];
        config.HiddenWristItems = ["host"];

        Assert.Equal(["host"], board.Current().Select(i => i.Id));
        Assert.Equal(["hrs"], board.Current(wrist: true).Select(i => i.Id));
    }

    [Fact]
    public void TheWristGetsItsOwnPush()
    {
        var (board, _, sent) = New();

        board.Register("hrs", Item("72 bpm"));
        board.Flush();

        Assert.Single(sent.On("statusItems"));
        Assert.Single(sent.On("wristStatus"));
    }

    /// <summary>手首で隠したものが変わっても手首には送らない（サイドバーには送る）。変わったかどうかは送り先ごとに見る。</summary>
    [Fact]
    public void AChangeHiddenOnlyFromTheWristReachesOnlyTheSidebar()
    {
        var (board, config, sent) = New();

        var hrs = Item("72 bpm");
        board.Register("host", Item("ログ"));
        board.Register("hrs", hrs);
        config.HiddenWristItems = ["hrs"];
        board.Flush();

        hrs.Text = "73 bpm";
        board.Flush();

        Assert.Equal(2, sent.On("statusItems").Length);
        Assert.Single(sent.On("wristStatus"));
    }
}
