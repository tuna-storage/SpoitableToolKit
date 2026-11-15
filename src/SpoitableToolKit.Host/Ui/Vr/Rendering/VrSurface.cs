using CefSharp;
using CefSharp.Enums;
using CefSharp.OffScreen;
using CefSharp.Structs;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Ui.Browser;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Host.Ui.Shell;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Pointing;
using Range = CefSharp.Structs.Range;

namespace SpoitableToolKit.Host.Ui.Vr.Rendering;

/// <summary>
/// VR に出すぶんの browser。画面外に描いて、GPU のテクスチャを取り出す。
/// 窓の browser とは別の 1 枚（サーフェスが 2 つある以上、同じ HTML が別々の JS コンテキストで動く。正となる状態は C# 側にあるので困らない）。
/// 絵は CPU を通らない: <c>OnAcceleratedPaint</c> が寄こす共有ハンドルを <see cref="VrSharedTexture"/> へ渡し、そちらが自前の 1 枚へ写し取る。
/// CPU バッファ経路は、1 回上げるたびに画面が乱れ、回数を増やすほど悪化したので使わない。
/// </summary>
internal sealed class VrSurface : IDisposable
{
    public const int Width = 1280;

    public const int Height = 720;

    /// <summary>
    /// CEF の <c>EVENTFLAG_PRECISION_SCROLLING_DELTA</c>（<c>cef_types_input.h</c>、<c>1 &lt;&lt; 14</c>）。CefSharp の列挙には無いので数で持つ（CefSharp は印をそのまま CEF へ渡す）。
    /// 付けると Chromium はホイールの量を「画素ちょうど」として扱い、滑らかに追いかけるアニメーションを掛けない（タッチパッドと同じ扱い）。
    /// OSR の <c>SendMouseWheelEvent</c> → <c>TranslateWebWheelEvent</c> → <c>TranslateUiEventModifiers</c> がこの印を <c>ui::EF_PRECISION_SCROLLING_DELTA</c> に直す。
    /// 量の直し方（Windows は 120 で 3 行 × 33.3 画素 = 100 画素）は印があっても同じなので、<see cref="VrLaser.DragScrollGain"/> はそのまま。
    /// </summary>
    private const CefEventFlags PrecisionScrollingDelta = (CefEventFlags)(1 << 14);

    private readonly ChromiumWebBrowser _browser;

    private readonly Handler _handler;

    private readonly UiBroadcast _broadcast;

    private volatile bool _hidden;

    /// <param name="width">画素数。既定はダッシュボードの板。手首は小さく渡す。</param>
    /// <param name="frameRate">1 秒に板へ渡す絵の上限。ヘッドセットの速さを渡す（<see cref="VrHost.FrameRate"/>）。</param>
    public VrSurface(
        string url, UiBroadcast broadcast, UiBridge bridge, VrSharedTexture shared, ILogger logger,
        int width = Width, int height = Height, IReadOnlySet<string>? channels = null,
        int frameRate = (int)VrHost.FallbackFrameRate)
    {
        _handler = new Handler(shared, logger, width, height);
        _broadcast = broadcast;

        _browser = new ChromiumWebBrowser(url, automaticallyCreateBrowser: false)
        {
            Size = new System.Drawing.Size(width, height),
        };

        // 窓の browser と同じ支度が要る。bind もテーマも push も browser 単位なので、1 つでも忘れると「VR だけ動かない / VR だけ明るい」になる。
        _browser.JavascriptObjectRepository.Register("stkHost", bridge);
        _browser.MenuHandler = new ContextMenuBlocker();
        _browser.RenderHandler = _handler;
        _browser.LoadingStateChanged += OnLoadingStateChanged;

        // 共有テクスチャを頼む。これを立てないと OnAcceleratedPaint は 1 度も呼ばれず、代わりに CPU バッファの OnPaint が来る。
        var windowInfo = CefSharp.Core.ObjectFactory.CreateWindowInfo();
        windowInfo.SetAsWindowless(0);
        windowInfo.SharedTextureEnabled = true;

        // 描くのはこちらが打ったときだけ（BeginFrame）。CEF に任せると windowless の上限 60（既定 30）で止まり、90 / 120 Hz のヘッドセットでは遅れて見える。
        // 打つのは VR の周で、周はヘッドセットの速さで回る。中身が変わっていなければ、打っても描かない。
        windowInfo.ExternalBeginFrameEnabled = true;

        // 渡す速さの上限は別にある。描かせても、CEF が板へ渡す絵は windowless の速さで間引かれる（90 Hz で描かせて画面を動かし続けても、届いた絵は 30 ちょうど = 既定の値だった）。
        // ヘッドセットの速さを渡すと 88〜90 枚届いた — 説明書きの上限は 60 だが、この CEF（151）では 90 も通った。
        var settings = CefSharp.Core.ObjectFactory.CreateBrowserSettings(true);
        settings.WindowlessFrameRate = frameRate;

        _browser.CreateBrowser(windowInfo, settings);
        broadcast.Attach(_browser, channels);
    }

    /// <summary>レーザーの 1 手を流し込む。座標はもう画面の画素（向きも直してある）。押しっぱなしの回数は数えない（レーザーで連打する道が無いので、いつも 1 回）。</summary>
    public void Send(VrPointerKind kind, int x, int y, VrPointerButton button, int wheelX, int wheelY)
    {
        if (Host() is not { } host) return;

        var at = new MouseEvent(x, y, CefEventFlags.None);

        var which = button switch
        {
            VrPointerButton.Right => MouseButtonType.Right,
            VrPointerButton.Middle => MouseButtonType.Middle,
            _ => MouseButtonType.Left,
        };

        switch (kind)
        {
            case VrPointerKind.Move:
                host.SendMouseMoveEvent(at, mouseLeave: false);
                break;

            case VrPointerKind.Down:
                // 押すたびにフォーカスを渡す。画面外に描く browser は、誰かが SetFocus するまでフォーカスを持たない（デスクトップの窓は Windows がやるが、こちらには窓が無い）。
                // 持たないと、文字の欄を押してもカーソルが出ず選べないように見える。毎回渡すのは、眠らせたあとなどで外れていても次の 1 押しで戻るように。デスクトップの窓のフォーカスには触らない。
                host.SetFocus(true);
                host.SendMouseClickEvent(at, which, mouseUp: false, clickCount: 1);
                break;

            case VrPointerKind.Up:
                host.SendMouseClickEvent(at, which, mouseUp: true, clickCount: 1);
                break;

            case VrPointerKind.Scroll:
                host.SendMouseWheelEvent(at, wheelX, wheelY);
                break;

            case VrPointerKind.Pan:
                host.SendMouseWheelEvent(new MouseEvent(x, y, PrecisionScrollingDelta), wheelX, wheelY);
                break;

            case VrPointerKind.Leave:
                host.SendMouseMoveEvent(at, mouseLeave: true);

                // カーソルはこちらで戻す。Blink は外れたときにカーソルを選び直さない（位置が分からない扱い）ので、手の形のまま残り、次に板へ入った周に押せない所でも「乗った」と震える。
                _handler.Cursor = CursorType.Pointer;
                break;
        }
    }

    /// <summary>いま押せる物の上か（CEF のカーソルが手の形か）。自前のレーザーが「乗った」で震わせるのに使う。ページに手を入れずに済む（<c>cursor: pointer</c> の物ならどれでも）。CEF のスレッドから書かれるので、周からは読むだけ。</summary>
    public bool OverClickable => _handler.Cursor == CursorType.Hand;

    /// <summary>いま掴める物の上か（CEF のカーソルが「掴む手」か: CSS の <c>cursor: grab</c> / <c>grabbing</c>）。自前のレーザーが、巻く板でも巻かずに押す・動かす・離すを送るのに使う（<see cref="VrLaserTarget.Grabbable"/>）。</summary>
    public bool OverGrab => _handler.Cursor is CursorType.Grab or CursorType.Grabbing;

    /// <summary>1 枚描かせる（中身が変わっていれば）。周ごとに呼ぶ — 呼ばないと 1 枚も描かない。眠らせている間は打たない（起こした周には打つ。<see cref="SetHidden"/> の描き直しの頼みもこれで描かれる）。</summary>
    public void BeginFrame()
    {
        if (_hidden || Host() is not { } host) return;

        host.SendExternalBeginFrame();
    }

    /// <summary>この面だけ画面を切り替える（手首のショートカット）。窓は切り替えない。受け口はモジュールの「自分の画面を出して」と同じ <c>showPanel</c>で、シェルの <c>selectView</c> は <c>log</c> と <c>settings</c> も受けるので、内蔵の画面にも使える。</summary>
    public void Show(string view) => Push("showPanel", new { module = view });

    /// <summary>この面にだけ報せを送る（受け取る報せの絞り込みは通らない）。</summary>
    public void Push(string channel, object payload) => _broadcast.PushTo(_browser, channel, payload);

    /// <summary>
    /// 見えていない間は眠らせる。描かない・タイマーを絞るので、GPU のタイルも手放せる。
    /// ⚠ 起こしたら描き直しを頼む。CEF は起こしただけでは描かないので、板には眠る前の絵が残ったままになる。
    /// </summary>
    public void SetHidden(bool hidden)
    {
        // browser が出来る前に頼まれることがある（面を作った直後）。覚えておいて、読み込み終わりで当てる。
        _hidden = hidden;

        if (Host() is not { } host) return;

        host.WasHidden(hidden);

        if (!hidden) host.Invalidate(PaintElementType.View);
    }

    /// <summary>CEF の browser。出来る前は null。⚠ <c>GetBrowser()</c> は出来る前だと null を返さず例外を投げる（面を作った直後に眠らせようとして、VR の周ごと止まった）ので、先に出来ているかを見る。</summary>
    private IBrowserHost? Host() => _browser.IsBrowserInitialized ? _browser.GetBrowser()?.GetHost() : null;

    public void Dispose()
    {
        _browser.LoadingStateChanged -= OnLoadingStateChanged;

        // 配る先から外す。外さないと、閉じた browser へ push し続ける（繋ぎ直すたびに面を作り直すので、外し忘れは 1 回ごとに溜まる）。
        _broadcast.Detach(_browser);
        _browser.Dispose();
    }

    private void OnLoadingStateChanged(object? sender, LoadingStateChangedEventArgs e)
    {
        if (e.IsLoading) return;

        _browser.LoadingStateChanged -= OnLoadingStateChanged;

        // CEF は Windows のテーマを見ないので、browser ごとに当てる。これを忘れると VR の面だけ明るいまま出る。
        _ = CefTheme.ApplyAsync(_browser);

        if (_hidden) Host()?.WasHidden(true);
    }

    /// <summary>描かれた先を受け取るだけの器。欲しいのは <see cref="OnAcceleratedPaint"/> 1 つで、残りは CEF が呼ぶので実装だけしておく。</summary>
    private sealed class Handler(VrSharedTexture shared, ILogger logger, int width, int height) : CefSharp.OffScreen.IRenderHandler
    {
        private bool _warned;

        public void Dispose()
        {
        }

        public ScreenInfo? GetScreenInfo() => new() { DeviceScaleFactor = 1.0F };

        public Rect GetViewRect() => new(0, 0, width, height);

        public bool GetScreenPoint(int viewX, int viewY, out int screenX, out int screenY)
        {
            screenX = viewX;
            screenY = viewY;

            return false;
        }

        public void OnAcceleratedPaint(PaintElementType type, Rect dirtyRect, AcceleratedPaintInfo acceleratedPaintInfo)
        {
            // 別レイヤーは受けない。ドロップダウン・ツールチップ・日時のカレンダーは板とは別の絵として来る。出したいものはページの中に描く方針。
            if (type != PaintElementType.View) return;

            // その場で写し取る。CEF はフレームごとに新しい共有テクスチャを作り、ここを抜けたら閉じる（あとから開くと E_INVALIDARG）。渡す先は自前の 1 枚なので、閉じられても困らない。
            shared.Copy(acceleratedPaintInfo.SharedTextureHandle);
        }

        /// <summary>CPU の絵。共有テクスチャが効いていれば 1 度も来ない。来たら頼んだ経路が使えていないということなので、1 回だけ残す。</summary>
        public void OnPaint(PaintElementType type, Rect dirtyRect, nint buffer, int width, int height)
        {
            if (_warned) return;

            _warned = true;
            logger.LogWarning("The VR surface fell back to CPU painting; the shared texture is not in use");
        }

        /// <summary>CEF が最後に言ってきたカーソル。変わったときにしか来ない。</summary>
        public volatile CursorType Cursor = CursorType.Pointer;

        public void OnCursorChange(nint cursor, CursorType type, CursorInfo customCursorInfo) => Cursor = type;

        public bool StartDragging(IDragData dragData, DragOperationsMask mask, int x, int y) => false;

        public void UpdateDragCursor(DragOperationsMask operation)
        {
        }

        public void OnPopupShow(bool show)
        {
        }

        public void OnPopupSize(Rect rect)
        {
        }

        public void OnImeCompositionRangeChanged(Range selectedRange, Rect[] characterBounds)
        {
        }

        public void OnVirtualKeyboardRequested(IBrowser browser, TextInputMode inputMode)
        {
        }
    }
}
