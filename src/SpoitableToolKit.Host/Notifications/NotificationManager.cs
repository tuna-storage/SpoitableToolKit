using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Host.Notifications.Banners;
using SpoitableToolKit.Host.Notifications.Confirms;
using SpoitableToolKit.Host.Notifications.History;
using SpoitableToolKit.Host.Notifications.Toasts;
using SpoitableToolKit.Host.Modules;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Push;

namespace SpoitableToolKit.Host.Notifications;

/// <summary>
/// 通知の面をここで決める。モジュールが選ぶのは段（どれくらい強く割り込むか）と種類（色とアイコン）だけ。
/// <para>
/// 窓の中に出す。別窓の最前面トーストにはしない。SteamVR の Steam オーバーレイでデスクトップを覗いているときに見えているのはその窓なので、
/// 外に飛び出した小窓は届かないし押しづらい。
/// </para>
/// <para>
/// 出し先は <see cref="UiBroadcast"/> なので、繋がっている全サーフェス（デスクトップ窓 / VR の窓 / 手首）に同じものが出て、どこから答えても 1 回だけ効く。
/// </para>
/// <para>
/// このセッションで出したものは履歴に残す（タイトルバーのベル・手首の通知の板）。
/// 時間切れになった確認は、<see cref="NotifyRequest.Later"/> があれば履歴から後で答えられる。
/// </para>
/// <para>
/// 段ごとに係（<see cref="ConfirmBoard"/>・<see cref="ToastBoard"/>・<see cref="BannerBoard"/>）へ渡す。履歴（<see cref="NotificationHistory"/>）は 3 つが同じものを使う。
/// どれも先に履歴へ書いてから画面へ出す（<c>notifyHistory</c> が先）。
/// </para>
/// </summary>
internal sealed class NotificationManager : INotificationManager
{
    internal const int HistoryLimit = NotificationHistory.Limit;

    private readonly ILogger<NotificationManager> _logger;
    private readonly NotificationHistory _history;
    private readonly ConfirmBoard _confirms;
    private readonly ToastBoard _toasts;
    private readonly BannerBoard _banners;

    public NotificationManager(UiBroadcast broadcast, HostConfig config, ILoggerFactory loggerFactory)
    {
        // 係は通知の logger で書く（ログのカテゴリはこのクラスのまま）。押されたものは Run が走らせる。
        _logger = loggerFactory.CreateLogger<NotificationManager>();
        _history = new NotificationHistory(broadcast);
        _confirms = new ConfirmBoard(broadcast, config, _history, _logger, Run);
        _toasts = new ToastBoard(broadcast, config, _history, _logger, Run);
        _banners = new BannerBoard(broadcast, _history, _logger, Run);
    }

    public Task<bool> PushModalAsync(NotifyRequest request, CancellationToken cancellationToken = default) =>
        _confirms.PushAsync(request, cancellationToken);

    public IToast PushToast(NotifyKind kind, string title, string? body = null, params NotifyAction[] actions) =>
        _toasts.Push(kind, title, body, actions);

    /// <summary>種類を選ばない分（<see cref="NotifyKind.Info"/>）。具体型から呼ぶ所のため — 既定のメソッドはインターフェース越しにしか見えない。</summary>
    public IToast PushToast(string title, string? body = null, params NotifyAction[] actions) =>
        PushToast(NotifyKind.Info, title, body, actions);

    internal void CloseToast(long id) => _toasts.Close(id);

    internal object[] LiveToasts() => _toasts.Live();

    internal void InvokeToastAction(long id, int index) => _toasts.Invoke(id, index);

    public void PushBanner(NotifyKind kind, string title, string? body = null, params NotifyAction[] actions) =>
        _banners.Push(kind, title, body, actions);

    public void PushBanner(string title, string? body = null, params NotifyAction[] actions) =>
        PushBanner(NotifyKind.Error, title, body, actions);

    internal void CloseBanner(long id) => _banners.Close(id);

    internal object[] LiveBanners() => _banners.Live();

    internal void InvokeBannerAction(long id, int index) => _banners.Invoke(id, index);

    internal object[] LiveAsks() => _confirms.Live();

    /// <summary>どのサーフェスから答えても、ここへ来る。二重に答えても最初の 1 回だけが効く。</summary>
    public void Answer(long id, bool accepted) => _confirms.Answer(id, accepted);

    public void OptOut(long id) => _confirms.OptOut(id);

    internal void AnswerLate(long seq) => _confirms.AnswerLate(seq);

    /// <summary>このセッションの履歴。新しいものが先。</summary>
    internal object[] History() => _history.Snapshot();

    /// <summary>アプリを畳むとき。待っている確認は「承諾されなかった」で終わらせる。</summary>
    public void CancelAll()
    {
        _confirms.CancelAll();

        // 時計を止める。畳んだあとに発火して、消えた画面へ push しに行かせない。
        _toasts.CloseAll();
    }

    /// <summary>画面へ渡す種類。アイコンのパスはここで検める — 外れたものを画面へ流すと、シェルの origin でモジュールの書いた字が属性に入る。</summary>
    internal static object KindPayload(NotifyKind kind) => new
    {
        id = kind.Id,
        tone = kind.Tone.ToString().ToLowerInvariant(),
        icon = NotifyKind.IsSafePath(kind.IconPath) ? kind.IconPath : NotifyKind.Info.IconPath,
    };

    /// <summary>押せるものは出したときに決まる。範囲の外は <c>null</c>（画面が古い場合の備え）。</summary>
    internal static NotifyAction? ActionAt(NotifyAction[] actions, int index) =>
        index >= 0 && index < actions.Length ? actions[index] : null;

    private void Run(NotifyAction action) =>
        ModuleCallback.Run(_logger, $"notification action '{action.Label}'", action.Invoke);
}
