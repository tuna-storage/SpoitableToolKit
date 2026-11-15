using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Host.Notifications;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Notifications;

public sealed class NotificationManagerTests
{
    private static NotificationManager NewNotify(HostConfig? config = null) =>
        new(new UiBroadcast(), config ?? new HostConfig(), NullLoggerFactory.Instance);

    private static NotifyRequest Ask(int timeoutMs = 30_000) => new()
    {
        Title = "動画の再生に失敗しました",
        Body = "https://example.com/watch",
        Timeout = TimeSpan.FromMilliseconds(timeoutMs),
    };


    [Fact]
    public async Task AcceptingReturnsTrue()
    {
        var notify = NewNotify();
        var asking = notify.PushModalAsync(Ask());

        notify.Answer(1, accepted: true);

        Assert.True(await asking);
    }

    [Fact]
    public async Task DecliningReturnsFalse()
    {
        var notify = NewNotify();
        var asking = notify.PushModalAsync(Ask());

        notify.Answer(1, accepted: false);

        Assert.False(await asking);
    }

    [Fact]
    public async Task ATimeoutCountsAsDeclining()
    {
        // 出しっぱなしにしない。VRChat を見ている間に溜まっていくため。
        var notify = NewNotify();

        Assert.False(await notify.PushModalAsync(Ask(timeoutMs: 150)));
    }

    [Fact]
    public async Task ASecondAnswerHasNoEffect()
    {
        // デスクトップ窓と VR オーバーレイの両方に出るので、二重に答えられる経路が普通にある。
        var notify = NewNotify();
        var asking = notify.PushModalAsync(Ask());

        notify.Answer(1, accepted: true);
        notify.Answer(1, accepted: false);

        Assert.True(await asking);
    }

    [Fact]
    public async Task ShutdownEndsAPendingPromptAsDeclined()
    {
        // 答えを待ったままモジュールを止めない。
        var notify = NewNotify();
        var asking = notify.PushModalAsync(Ask());

        notify.CancelAll();

        Assert.False(await asking);
    }

    [Fact]
    public async Task CancellingFromTheCallerAlsoEndsIt()
    {
        var notify = NewNotify();
        using var cancellation = new CancellationTokenSource();

        var asking = notify.PushModalAsync(Ask(), cancellation.Token);
        await cancellation.CancelAsync();

        Assert.False(await asking);
    }

    [Fact]
    public void AToastDoesNotWaitForAnAnswer()
    {
        var notify = NewNotify();

        notify.PushToast("コピーしました", "1080p 映像+音声（HLS）");
        notify.Answer(0, accepted: true);
    }

    /// <summary>出しておく時間はホストが決める（モジュールの <c>NotifyRequest</c> は既定で秒数を持たない）。既定は 15 秒。</summary>
    [Fact]
    public async Task WithoutASecondsValueTheHostDefaultIsUsed()
    {
        var notify = NewNotify();

        // 答えないまま既定の時間が過ぎるのを待たない（15 秒かかる）。
        var asking = notify.PushModalAsync(new NotifyRequest { Title = "秒数の指定なし" });

        notify.Answer(1, accepted: true);

        Assert.True(await asking);
    }

    [Fact]
    public async Task TheSecondsValueCanBeChangedInTheSettings()
    {
        var notify = NewNotify(new HostConfig { ConfirmSeconds = 3 });

        // 3 秒で「いいえ」になる。指定しないままなら 15 秒なので、ここで返るのは設定が効いた証拠。
        var accepted = await notify.PushModalAsync(new NotifyRequest { Title = "短い" }).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(accepted);
    }

    private static (NotificationManager Notify, Recording Sent) NewRecorded()
    {
        var sent = new Recording();
        return (new NotificationManager(sent, new HostConfig(), NullLoggerFactory.Instance), sent);
    }

    private static long SeqOf(object entry) => (long)Recording.Field(entry, "seq")!;

    [Fact]
    public void AnAskCarriesItsKind()
    {
        var (notify, _) = NewRecorded();

        _ = notify.PushModalAsync(new NotifyRequest { Title = "確認", Kind = NotifyKind.Warn });

        var kind = Recording.Field(Assert.Single(notify.LiveAsks()), "kind")!;
        Assert.Equal("warn", Recording.Field(kind, "id"));
        Assert.Equal("amber", Recording.Field(kind, "tone"));
        Assert.Equal(NotifyKind.Warn.IconPath, Recording.Field(kind, "icon"));
    }

    [Fact]
    public void AnAskWithoutAnOptOutHasNoOptOutLabel()
    {
        var (notify, _) = NewRecorded();

        _ = notify.PushModalAsync(Ask());

        Assert.Null(Recording.Field(Assert.Single(notify.LiveAsks()), "optOutLabel"));
    }

    [Fact]
    public void AnAskWithAnOptOutShowsItsLabel()
    {
        var (notify, _) = NewRecorded();

        _ = notify.PushModalAsync(Ask() with { OptOut = new NotifyAction("今後は聞かない", () => { }) });

        Assert.Equal("今後は聞かない", Recording.Field(Assert.Single(notify.LiveAsks()), "optOutLabel"));
    }

    /// <summary>「今後は聞かない」は、答えを「承諾されなかった」で閉じてからモジュールの処理を走らせる（承諾で返ると、その動作が実行されてしまう）。</summary>
    [Fact]
    public async Task OptingOutClosesTheAskAsDeclinedThenRunsTheAction()
    {
        var (notify, sent) = NewRecorded();
        var ran = new TaskCompletionSource<int>();
        var asking = notify.PushModalAsync(Ask() with
        {
            OptOut = new NotifyAction("今後は聞かない", () => ran.SetResult(notify.LiveAsks().Length)),
        });

        notify.OptOut(1);

        Assert.False(await asking);

        Assert.Equal(0, await ran.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Single(sent.On("notifyClosed"));
        Assert.Equal("optedOut", Recording.Field(Assert.Single(notify.History()), "state"));
    }

    [Fact]
    public async Task OptingOutTwiceRunsTheActionOnce()
    {
        var (notify, _) = NewRecorded();
        var runs = 0;
        var first = new TaskCompletionSource();
        var asking = notify.PushModalAsync(Ask() with
        {
            OptOut = new NotifyAction("今後は聞かない", () =>
            {
                Interlocked.Increment(ref runs);
                first.TrySetResult();
            }),
        });

        notify.OptOut(1);
        notify.OptOut(1);
        await asking;
        await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        Assert.Equal(1, Volatile.Read(ref runs));
    }

    [Fact]
    public void OptingOutOfAnAskWithoutOneDoesNothing()
    {
        var (notify, sent) = NewRecorded();

        _ = notify.PushModalAsync(Ask());
        notify.OptOut(1);

        Assert.Single(notify.LiveAsks());
        Assert.Empty(sent.On("notifyClosed"));
    }

    [Fact]
    public async Task OptingOutAfterAnAnswerDoesNotRunTheAction()
    {
        var (notify, _) = NewRecorded();
        var ran = false;
        var asking = notify.PushModalAsync(Ask() with { OptOut = new NotifyAction("今後は聞かない", () => ran = true) });

        notify.Answer(1, accepted: true);
        notify.OptOut(1);
        await Task.Delay(100);

        Assert.True(await asking);
        Assert.False(ran);
    }

    [Fact]
    public async Task AnAnsweredAskLeavesTheLiveList()
    {
        var (notify, sent) = NewRecorded();
        var asking = notify.PushModalAsync(Ask());

        Assert.Single(notify.LiveAsks());
        notify.Answer(1, accepted: true);
        await asking;

        Assert.Empty(notify.LiveAsks());
        Assert.Single(sent.On("notifyClosed"));
    }

    [Fact]
    public void ALiveAskReportsTheTotalAndTheTimeLeft()
    {
        var (notify, _) = NewRecorded();

        _ = notify.PushModalAsync(Ask(timeoutMs: 20_000));

        var ask = Assert.Single(notify.LiveAsks());
        Assert.Equal(20_000, Recording.Field(ask, "totalMs"));
        var left = (int)Recording.Field(ask, "timeoutMs")!;
        Assert.InRange(left, 1, 20_000);
    }

    /// <summary>時間切れで答えそびれた確認は、履歴から 1 回だけ後で答えられる（二度走ると、同じ動作が二重に起きる）。</summary>
    [Fact]
    public async Task AnExpiredAskCanBeAnsweredLateOnce()
    {
        var (notify, _) = NewRecorded();
        var runs = 0;
        var ran = new TaskCompletionSource();
        var asking = notify.PushModalAsync(Ask(timeoutMs: 50) with
        {
            Later = new NotifyAction("あとで解決", () =>
            {
                Interlocked.Increment(ref runs);
                ran.TrySetResult();
            }),
        });

        Assert.False(await asking);

        var entry = Assert.Single(notify.History());
        Assert.Equal("expired", Recording.Field(entry, "state"));
        Assert.Equal(true, Recording.Field(entry, "later"));

        notify.AnswerLate(SeqOf(entry));
        notify.AnswerLate(SeqOf(entry));
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        Assert.Equal(1, Volatile.Read(ref runs));
        var after = Assert.Single(notify.History());
        Assert.Equal("answeredLate", Recording.Field(after, "state"));
        Assert.Equal(false, Recording.Field(after, "later"));
    }

    /// <summary>時間切れを画面へ知らせる 1 件（<c>notifyHistory</c>）に、もう「後で答えられる」が載っている（開いているベルの一覧はこの push で書き換わる）。</summary>
    [Fact]
    public async Task TheExpiryReachesTheScreenAlreadyAnswerableLate()
    {
        var (notify, sent) = NewRecorded();

        Assert.False(await notify.PushModalAsync(Ask(timeoutMs: 50) with { Later = new NotifyAction("あとで解決", () => { }) }));

        var expired = Assert.Single(sent.On("notifyHistory"), s => Equals(Recording.Field(s.Payload, "state"), "expired"));
        Assert.Equal(true, Recording.Field(expired.Payload, "later"));
    }

    [Fact]
    public async Task ADeclinedAskCannotBeAnsweredLate()
    {
        var (notify, _) = NewRecorded();
        var ran = false;
        var asking = notify.PushModalAsync(Ask() with { Later = new NotifyAction("あとで", () => ran = true) });

        notify.Answer(1, accepted: false);
        await asking;
        var entry = Assert.Single(notify.History());
        notify.AnswerLate(SeqOf(entry));
        await Task.Delay(100);

        Assert.False(ran);
        Assert.Equal("declined", Recording.Field(entry, "state"));
        Assert.Equal(false, Recording.Field(entry, "later"));
    }

    [Fact]
    public async Task AnOptedOutAskCannotBeAnsweredLate()
    {
        var (notify, _) = NewRecorded();
        var ran = false;
        var asking = notify.PushModalAsync(Ask() with
        {
            OptOut = new NotifyAction("今後は聞かない", () => { }),
            Later = new NotifyAction("あとで", () => ran = true),
        });

        notify.OptOut(1);
        await asking;
        notify.AnswerLate(SeqOf(Assert.Single(notify.History())));
        await Task.Delay(100);

        Assert.False(ran);
    }

    [Fact]
    public async Task APendingAskCannotBeAnsweredLate()
    {
        var (notify, _) = NewRecorded();
        var ran = false;
        _ = notify.PushModalAsync(Ask() with { Later = new NotifyAction("あとで", () => ran = true) });

        var entry = Assert.Single(notify.History());
        notify.AnswerLate(SeqOf(entry));
        await Task.Delay(100);

        Assert.False(ran);
        Assert.Equal("pending", Recording.Field(entry, "state"));
        Assert.Equal(false, Recording.Field(entry, "later"));
    }

    [Fact]
    public void ANotificationIsRecordedInTheHistoryWithItsType()
    {
        var (notify, sent) = NewRecorded();

        notify.PushToast(NotifyKind.Done, "コピーしました", "本文");
        notify.PushBanner(NotifyKind.Error, "読み込めません");

        var entries = notify.History();
        Assert.Equal(["banner", "toast"], entries.Select(e => (string)Recording.Field(e, "type")!));
        Assert.Equal("コピーしました", Recording.Field(entries[1], "title"));
        Assert.Equal("本文", Recording.Field(entries[1], "body"));
        Assert.Equal("done", Recording.Field(Recording.Field(entries[1], "kind")!, "id"));
        Assert.Equal(2, sent.On("notifyHistory").Length);
    }

    [Fact]
    public void TheHistoryIsNewestFirst()
    {
        var (notify, _) = NewRecorded();

        notify.PushBanner("一つ目");
        notify.PushBanner("二つ目");
        notify.PushBanner("三つ目");

        var entries = notify.History();
        Assert.Equal(["三つ目", "二つ目", "一つ目"], entries.Select(e => (string)Recording.Field(e, "title")!));
        Assert.True(SeqOf(entries[0]) > SeqOf(entries[1]));
    }

    /// <summary>通知が溜まり続けても古いものから落とす。落ちたものの「後で答える」も一緒に消える。</summary>
    [Fact]
    public async Task TheHistoryKeepsOnlyTheNewestTwoHundred()
    {
        var (notify, _) = NewRecorded();
        var ran = false;
        await notify.PushModalAsync(Ask(timeoutMs: 30) with { Later = new NotifyAction("あとで", () => ran = true) });
        var expiredSeq = SeqOf(Assert.Single(notify.History()));

        for (var i = 0; i < NotificationManager.HistoryLimit; i++) notify.PushBanner($"#{i}");

        var entries = notify.History();
        Assert.Equal(NotificationManager.HistoryLimit, entries.Length);
        Assert.Equal("#199", Recording.Field(entries[0], "title"));
        Assert.Equal("#0", Recording.Field(entries[^1], "title"));
        Assert.DoesNotContain(entries, e => SeqOf(e) == expiredSeq);

        notify.AnswerLate(expiredSeq);
        await Task.Delay(100);
        Assert.False(ran);
    }

    [Fact]
    public void ChangingAToastTitleRewritesItsHistoryEntry()
    {
        var (notify, sent) = NewRecorded();

        var toast = notify.PushToast("取得しています", "40MB");
        var seq = SeqOf(Assert.Single(notify.History()));
        toast.Title = "取得しました";

        var entry = Assert.Single(notify.History());
        Assert.Equal(seq, SeqOf(entry));
        Assert.Equal("取得しました", Recording.Field(entry, "title"));
        Assert.Equal("40MB", Recording.Field(entry, "body"));

        var pushes = sent.On("notifyHistory");
        Assert.Equal(2, pushes.Length);
        Assert.Equal(seq, SeqOf(pushes[1].Payload));
        Assert.Equal("取得しました", Recording.Field(pushes[1].Payload, "title"));
    }

    [Fact]
    public void ChangingAToastBodyRewritesItsHistoryEntry()
    {
        var (notify, _) = NewRecorded();

        var toast = notify.PushToast("取得しています", "10%");
        toast.Body = "90%";

        Assert.Equal("90%", Recording.Field(Assert.Single(notify.History()), "body"));
    }

    [Fact]
    public void ProgressAloneDoesNotRewriteTheHistory()
    {
        var (notify, sent) = NewRecorded();

        var toast = notify.PushToast("取得しています");
        toast.Progress = 0.2;
        toast.Progress = 0.6;

        Assert.Single(sent.On("notifyHistory"));
    }

    [Fact]
    public async Task AnAskIsRecordedAndFollowsItsAnswer()
    {
        var (notify, _) = NewRecorded();
        var asking = notify.PushModalAsync(Ask() with { ConfirmLabel = "解決してコピー" });

        var pending = Assert.Single(notify.History());
        Assert.Equal("ask", Recording.Field(pending, "type"));
        Assert.Equal("pending", Recording.Field(pending, "state"));
        Assert.Equal("解決してコピー", Recording.Field(pending, "confirmLabel"));

        notify.Answer(1, accepted: true);
        await asking;

        Assert.Equal("accepted", Recording.Field(Assert.Single(notify.History()), "state"));
    }

    [Fact]
    public async Task ADeclinedAskIsRecordedAsDeclined()
    {
        var (notify, _) = NewRecorded();
        var asking = notify.PushModalAsync(Ask());

        notify.Answer(1, accepted: false);
        await asking;

        Assert.Equal("declined", Recording.Field(Assert.Single(notify.History()), "state"));
    }

    /// <summary>アプリを畳んで終わらせたものを「時間切れ」と見せて、後から答えられる形にしない。</summary>
    [Fact]
    public async Task ShuttingDownRecordsADeclineNotAnExpiry()
    {
        var (notify, _) = NewRecorded();
        var asking = notify.PushModalAsync(Ask() with { Later = new NotifyAction("あとで", () => { }) });

        notify.CancelAll();
        await asking;

        var entry = Assert.Single(notify.History());
        Assert.Equal("declined", Recording.Field(entry, "state"));
        Assert.Equal(false, Recording.Field(entry, "later"));
    }
}
