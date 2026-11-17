using System.Reflection;
using CefSharp;
using SpoitableToolKit.Host.Ui.Push;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Push;

/// <summary>
/// push はつながっている面すべてへ送る。受け取れるのを見てから送るまでの間に、別のスレッドがその面を畳むことがある（窓をトレイにしまう・VR の面を片付ける）。
/// そのとき投げたものでは止まらず、残りの面へ送る。面は CEF を立てずに、CefSharp の口だけを真似たもの（<see cref="FakeBrowser"/>）。
/// </summary>
public sealed class UiBroadcastTests
{
    /// <summary>
    /// CefSharp の口を真似る。呼ばれたメンバーの名前で答えを決め、決めていないものは既定値を返す。
    /// 送った JS は <see cref="Scripts"/> に溜まる（面の中の browser の、さらに中の main frame が受ける）。
    /// </summary>
    public class FakeBrowser : DispatchProxy
    {
        public Dictionary<string, Func<object?[]?, object?>> Answers { get; } = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method is null) return null;
            if (Answers.TryGetValue(method.Name, out var answer)) return answer(args);
            return method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
        }

        public static T Make<T>(Action<FakeBrowser> setUp) where T : class
        {
            var proxy = DispatchProxy.Create<T, FakeBrowser>();
            setUp((FakeBrowser)(object)proxy);
            return proxy;
        }

        /// <summary>JS を受け取れる面。送られた JS を <paramref name="scripts"/> に足す。</summary>
        public static IWebBrowser Alive(List<string> scripts)
        {
            var frame = Make<IFrame>(f =>
            {
                f.Answers["get_IsValid"] = _ => true;
                f.Answers["get_IsMain"] = _ => true;
                f.Answers["ExecuteJavaScriptAsync"] = args =>
                {
                    scripts.Add((string)args![0]!);
                    return null;
                };
            });
            var browser = Make<IBrowser>(b =>
            {
                b.Answers["get_MainFrame"] = _ => frame;
                b.Answers["get_IsDisposed"] = _ => false;
            });
            return Make<IWebBrowser>(w =>
            {
                w.Answers["get_CanExecuteJavascriptInMainFrame"] = _ => true;
                w.Answers["get_IsBrowserInitialized"] = _ => true;
                w.Answers["get_BrowserCore"] = _ => browser;
                w.Answers["GetBrowser"] = _ => browser;
            });
        }

        /// <summary>受け取れると答えたあとに、中の browser が無くなった面（CefSharp は「IBrowser instance is null」を投げる）。</summary>
        public static IWebBrowser LostItsBrowser(bool disposed) => Make<IWebBrowser>(w =>
        {
            w.Answers["get_CanExecuteJavascriptInMainFrame"] = _ => true;
            w.Answers["get_IsBrowserInitialized"] = _ => true;
            w.Answers["get_IsDisposed"] = _ => disposed;
            w.Answers["get_BrowserCore"] = _ => null;
            w.Answers["GetBrowser"] = _ => null;
        });
    }

    /// <summary>前の面が畳まれて投げても、後ろの面には届く。push を出した側（設定の「適用」など）も失敗しない。</summary>
    [Fact]
    public void AScreenPutAwayMidPushIsSkippedAndTheRestStillGetIt()
    {
        var scripts = new List<string>();
        var broadcast = new UiBroadcast();
        broadcast.Attach(FakeBrowser.LostItsBrowser(disposed: true));
        broadcast.Attach(FakeBrowser.Alive(scripts));

        var thrown = Record.Exception(() => broadcast.Push(ConfigChangedPush.Channel, new ConfigChangedPush(Language: false)));

        Assert.Null(thrown);
        var script = Assert.Single(scripts);
        Assert.Contains("\"configChanged\"", script);
        Assert.Contains("{\"language\":false}", script);
    }

    /// <summary>畳んでいない面が投げたものは隠さない（壊れているのに気付けなくなる）。</summary>
    [Fact]
    public void AScreenThatFailsWithoutBeingPutAwayStillFails()
    {
        var broadcast = new UiBroadcast();
        broadcast.Attach(FakeBrowser.LostItsBrowser(disposed: false));

        Assert.ThrowsAny<Exception>(() => broadcast.Push(ConfigChangedPush.Channel, new ConfigChangedPush(Language: false)));
    }

    /// <summary>1 つの面へ送る口（<see cref="UiBroadcast.PushTo"/>）も同じ。</summary>
    [Fact]
    public void PushingToAScreenPutAwayMidPushDoesNotThrow()
    {
        var thrown = Record.Exception(() => new UiBroadcast().PushTo(FakeBrowser.LostItsBrowser(disposed: true), "showPanel", new { module = "hrs" }));

        Assert.Null(thrown);
    }
}
