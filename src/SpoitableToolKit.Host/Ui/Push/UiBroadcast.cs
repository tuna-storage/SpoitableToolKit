using System.Text.Json;
using CefSharp;
using SpoitableToolKit.Host.Ui.Shell;

namespace SpoitableToolKit.Host.Ui.Push;

/// <summary>
/// C# 側の状態を、繋がっている全ての UI サーフェスへ push する。
/// <see cref="Push"/> が virtual なのは試験のため。繋がっているサーフェスが 0 ならここは黙って捨てるので、push の道が壊れても誰も気付かない。
/// デスクトップ窓と VR オーバーレイは別々の <see cref="IWebBrowser"/>＝別々の JS コンテキストなので、状態は必ずここを通して両方へ流す。
/// </summary>
/// </summary>
internal class UiBroadcast
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Lock _gate = new();
    private readonly List<(IWebBrowser Browser, IReadOnlySet<string>? Channels)> _targets = [];

    /// <param name="channels">受け取る報せを絞る。null なら全部。手首の板はログの行を使わないので、絞らないと 1 セッションで 1 万行を超える push を使わないのに捌くことになる。</param>
    public void Attach(IWebBrowser browser, IReadOnlySet<string>? channels = null)
    {
        lock (_gate) _targets.Add((browser, channels));
    }

    public void Detach(IWebBrowser browser)
    {
        lock (_gate) _targets.RemoveAll(t => ReferenceEquals(t.Browser, browser));
    }

    /// <summary>バックグラウンドスレッドから呼んでよい（CefSharp が CEF の UI スレッドへ回す）。</summary>
    public virtual void Push(string channel, object payload)
    {
        IWebBrowser[] targets;
        lock (_gate)
        {
            targets = [.. _targets.Where(t => t.Channels?.Contains(channel) ?? true).Select(t => t.Browser)];
        }

        if (targets.Length == 0) return;

        var script = Script(channel, payload);

        foreach (var target in targets) Run(target, script);
    }

    /// <summary>1 つの面にだけ送る。手首のショートカットでダッシュボードの画面だけ切り替えるときに使う（全部へ送るとデスクトップの窓まで切り替わる）。</summary>
    public virtual void PushTo(IWebBrowser target, string channel, object payload) =>
        Run(target, Script(channel, payload));

    /// <summary>画面へ渡す JSON（プロパティの名前は camelCase）。push と、record を返す橋の口（<see cref="UiBridge.GetModuleJobs"/> など）が同じこれを使うので、同じ record はどちらの道でも同じ形で画面に届く（CefSharp に record のまま渡すと、名前も型の扱いも別の決まりになる）。</summary>
    internal static string Json(object payload) => JsonSerializer.Serialize(payload, payload.GetType(), Options);

    private static string Script(string channel, object payload) =>
        $"window.stk && window.stk.receive({JsonSerializer.Serialize(channel)}, {Json(payload)});";

    /// <summary>
    /// ページがまだ JS を受け取れない間は黙って捨てる。UI 側は接続時に getStatus() で現在値を取り直すので、取りこぼしても追いつける。
    /// 受け取れるのを見てから送るまでの間に、別のスレッドがその面を畳むことがある（窓をトレイにしまう・VR の面を片付ける）。
    /// そのとき投げたもの（破棄済み・中の browser がもう無い）は、その面だけ飛ばして残りの面へ送る。投げると後ろの面に届かず、push を出した側
    /// （設定の「適用」など）まで失敗したことになる。畳んでいない面が投げたものは、今までどおり上げる。
    /// </summary>
    private static void Run(IWebBrowser target, string script)
    {
        if (!target.CanExecuteJavascriptInMainFrame) return;

        try
        {
            target.ExecuteScriptAsync(script);
        }
        catch (Exception) when (target.IsDisposed)
        {
            // 畳んだ面。画面ごと無くなったので、送る先も無い。
        }
    }
}
