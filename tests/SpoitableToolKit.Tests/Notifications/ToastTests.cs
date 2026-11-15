using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Host.Notifications;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Notifications;

/// <summary>正はホストにあり、画面はそれを写すだけ（画面で消えたものが更新で生き返らないよう、消える時計もホスト側が持つ）。<see cref="UiBroadcast"/> は繋がっているサーフェスが 0 なら黙って捨てるので、何が流れたかは差し替えて見る。</summary>
public sealed class ToastTests
{
    private static (NotificationManager Notify, Recording Sent) New(int toastSeconds = 30) =>
        New(new Logs(), toastSeconds);

    private static (NotificationManager Notify, Recording Sent) New(Logs logs, int toastSeconds = 30)
    {
        var config = new HostConfig { ToastSeconds = toastSeconds };
        var sent = new Recording();

        return (new NotificationManager(config: config, broadcast: sent, loggerFactory: logs), sent);
    }

    [Fact]
    public void PushingSendsItToTheScreen()
    {
        var (notify, sent) = New();

        notify.PushToast("変換を始めました", "sample.mp4");

        var toast = Assert.Single(sent.On("toast"));
        Assert.Equal("変換を始めました", Recording.Field(toast.Payload, "title"));
        Assert.Equal("sample.mp4", Recording.Field(toast.Payload, "body"));

        Assert.Null(Recording.Field(toast.Payload, "progress"));
    }

    [Fact]
    public void UpdatingResendsItUnderTheSameId()
    {
        var (notify, sent) = New();

        var toast = notify.PushToast("変換中");
        toast.Title = "書き出し中";

        var updates = sent.On("toast");
        Assert.Equal(2, updates.Length);
        Assert.Equal(Recording.Field(updates[0].Payload, "id"), Recording.Field(updates[1].Payload, "id"));
        Assert.Equal("書き出し中", Recording.Field(updates[1].Payload, "title"));
    }

    /// <summary>同じ値を入れ直しても画面は動かない。心拍のように連打されるものへの備え。</summary>
    [Fact]
    public void SettingTheSameValueSendsNothing()
    {
        var (notify, sent) = New();

        var toast = notify.PushToast("変換中");
        toast.Title = "変換中";

        Assert.Single(sent.On("toast"));
    }

    [Fact]
    public void ProgressIsSentOnlyWhenThePercentageMoves()
    {
        var (notify, sent) = New();

        var toast = notify.PushToast("取得中");
        for (var i = 0; i <= 1000; i++) toast.Progress = i / 1000.0;

        // 最初の 1 通 + 0% から 100% までの 101 段。
        Assert.Equal(102, sent.On("toast").Length);
    }

    [Fact]
    public void AProgressValueOutsideTheRangeIsClamped()
    {
        var (notify, _) = New();

        var toast = notify.PushToast("取得中");

        toast.Progress = -1;
        Assert.Equal(0, toast.Progress);

        toast.Progress = 5;
        Assert.Equal(1, toast.Progress);
    }

    /// <summary><c>using</c> は完了を入れた直後に抜けるので、ここで消すと「終わりました」が誰にも見えない。</summary>
    [Fact]
    public void DisposingAfterCompletionDoesNotRemoveIt()
    {
        var (notify, sent) = New();

        var toast = notify.PushToast("変換中");
        toast.Progress = 1;
        toast.Dispose();

        Assert.Empty(sent.On("toastClosed"));
        Assert.Single(notify.LiveToasts());
    }

    [Fact]
    public void DisposingPartWayThroughRemovesIt()
    {
        var (notify, sent) = New();

        var toast = notify.PushToast("変換中");
        toast.Progress = 0.4;
        toast.Dispose();

        Assert.Single(sent.On("toastClosed"));
        Assert.Empty(notify.LiveToasts());
    }

    [Fact]
    public void ClosingTwiceSendsOnlyOne()
    {
        var (notify, sent) = New();

        var toast = notify.PushToast("変換を始めました");
        toast.Dispose();
        toast.Dispose();
        notify.CloseToast(1);

        Assert.Single(sent.On("toastClosed"));
    }

    /// <summary>途中で開いた画面（読み込み直し / VR オーバーレイ）が追いつくための一覧。</summary>
    [Fact]
    public void LiveOnesCanBeListed()
    {
        var (notify, sent) = New();

        var first = notify.PushToast("取得中");
        notify.PushToast("変換中");
        first.Progress = 0.5;

        var live = notify.LiveToasts();
        Assert.Equal(2, live.Length);
        Assert.Contains(live, t => Equals(Recording.Field(t, "progress"), 0.5));

        first.Dispose();
        Assert.Single(notify.LiveToasts());
    }

    /// <summary>終わりが定義されたものを途中で消さない。<c>0</c> でも止まる（長さが分からない仕事のため）。</summary>
    [Fact]
    public async Task SettingProgressStopsItFromDismissingItself()
    {
        var (notify, sent) = New(toastSeconds: 1);

        notify.PushToast("すぐ消える");
        var working = notify.PushToast("取得中");
        working.Progress = 0;

        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Single(sent.On("toastClosed"));

        var live = Assert.Single(notify.LiveToasts());
        Assert.Equal("取得中", Recording.Field(live, "title"));
    }

    [Fact]
    public void ActionsAreHandedToTheScreen()
    {
        var (notify, sent) = New();

        notify.PushToast("書き出しました", "hr.csv", new NotifyAction("フォルダを開く", () => { }));

        var actions = (object[])Recording.Field(Assert.Single(sent.On("toast")).Payload, "actions")!;
        var action = Assert.Single(actions);
        Assert.Equal(0, Recording.Field(action, "index"));
        Assert.Equal("フォルダを開く", Recording.Field(action, "label"));
    }

    /// <summary>押されたら閉じる。決着を付けるためのものなので、残っていると押し直せてしまう。</summary>
    [Fact]
    public async Task AnActionRunsThenCloses()
    {
        var (notify, sent) = New();
        var ran = new TaskCompletionSource();

        notify.PushToast("書き出しました", actions: new NotifyAction("開く", ran.SetResult));
        notify.InvokeToastAction(1, 0);

        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(sent.On("toastClosed"));
        Assert.Empty(notify.LiveToasts());
    }

    /// <summary>画面が古いと、もう無い番号を押してくることがある。</summary>
    [Fact]
    public void AnActionIndexOutOfRangeDoesNothing()
    {
        var (notify, sent) = New();

        notify.PushToast("書き出しました", actions: new NotifyAction("開く", () => { }));
        notify.InvokeToastAction(1, 3);
        notify.InvokeToastAction(999, 0);

        Assert.Empty(sent.On("toastClosed"));
        Assert.Single(notify.LiveToasts());
    }

    /// <summary>落とすと CEF から回ってきたスレッドごと道連れになる。</summary>
    [Fact]
    public async Task AThrowingActionIsLoggedNotFatal()
    {
        var logs = new Logs();
        var (notify, _) = New(logs);
        var reached = new TaskCompletionSource();

        notify.PushToast("書き出しました", actions: new NotifyAction("開く", () =>
        {
            reached.SetResult();
            throw new InvalidOperationException("わざと");
        }));

        notify.InvokeToastAction(1, 0);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 握り潰すだけでは駄目（残っていないと「押したのに何も起きない」しか手掛かりが無い）。
        for (var attempt = 0; attempt < 50; attempt++)
        {
            lock (logs.Errors)
                if (logs.Errors.Count > 0) return;

            await Task.Delay(20);
        }

        Assert.Fail("押した先の例外がログに残っていない");
    }

    [Fact]
    public async Task OnCompletionItLingersBrieflyThenGoes()
    {
        var (notify, sent) = New(toastSeconds: 1);

        var toast = notify.PushToast("取得中");
        toast.Progress = 0.5;
        toast.Progress = 1;

        // 入れた瞬間には消えない。「終わりました」を見せる時間がある。
        Assert.Empty(sent.On("toastClosed"));

        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Single(sent.On("toastClosed"));
        Assert.Empty(notify.LiveToasts());
    }

    /// <summary>取得は最後の塊で 100% になり、ファイルを置き終えてからもう一度 100% を入れる。2 回目で時計を止めると「取得しました」が出続ける。</summary>
    [Fact]
    public async Task CompletingTwiceStillLingersThenGoes()
    {
        var (notify, sent) = New(toastSeconds: 1);

        var toast = notify.PushToast("取得中");
        toast.Progress = 1;
        toast.Title = "取得しました";
        toast.Progress = 1;

        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Single(sent.On("toastClosed"));
        Assert.Empty(notify.LiveToasts());
    }

    /// <summary>戻ると ✕ でも消せない札になる（閉じる頼みも時計も、もう居ないものには空振りする）。</summary>
    [Fact]
    public void AClosedToastDoesNotComeBackWhenUpdated()
    {
        var (notify, sent) = New();

        var toast = notify.PushToast("取得中");
        toast.Progress = 0.2;
        notify.CloseToast((long)Recording.Field(sent.On("toast").First().Payload, "id")!);
        var pushed = sent.On("toast").Count();

        toast.Progress = 0.6;
        toast.Title = "取得しました";
        toast.Body = "deno";

        Assert.Equal(pushed, sent.On("toast").Count());
    }

    /// <summary>出しておく秒数は「読めれば足りる」長さに合わせてあるので、押せるものにそのまま使うと押す前に消える。</summary>
    [Fact]
    public async Task OneWithActionsStaysUpLonger()
    {
        var (notify, sent) = New(toastSeconds: 1);

        notify.PushToast("見るだけ");
        notify.PushToast("書き出しました", actions: new NotifyAction("フォルダを開く", () => { }));

        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Single(sent.On("toastClosed"));

        var live = Assert.Single(notify.LiveToasts());
        Assert.Equal("書き出しました", Recording.Field(live, "title"));
    }

    /// <summary>畳むときに時計を残さない。消えた画面へ後から push しに行かせない。</summary>
    [Fact]
    public void ShutdownRemovesEveryLiveToast()
    {
        var (notify, sent) = New();

        notify.PushToast("ひとつめ");
        notify.PushToast("ふたつめ");
        notify.CancelAll();

        Assert.Equal(2, sent.On("toastClosed").Length);
        Assert.Empty(notify.LiveToasts());
    }
}
