using System.Runtime.InteropServices;
using CefSharp;
using CefSharp.WinForms;
using SpoitableToolKit.Host.Ui.Browser;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Host.Ui.Shell;

namespace SpoitableToolKit.Host.Ui.Desktop;

/// <summary>
/// デスクトップ側の窓。VR オーバーレイ（OSR）とは別インスタンスの windowed CEF を使う。windowed にするのは日本語 IME・テキスト選択・右クリック・DPI を CEF 任せにできるため。
/// トレイにしまうときは閉じて破棄する（browser ごと落とす）。開くたびに作り直すのも、閉じられかけたときに何をするかを決めるのも <see cref="DesktopApp"/>。
/// ここのメソッドは UI のスレッドから呼ぶ（<see cref="DesktopApp"/> が渡す）。
/// </summary>
internal sealed class DesktopWindow : Form
{
    /// <summary>問いを出せたかの答えを待つ長さ。返らなければ、出せなかったものとして扱う（窓は畳む）。</summary>
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(2);

    private readonly ChromiumWebBrowser _browser;
    private readonly UiBroadcast _broadcast;
    private readonly bool _offscreen;

    /// <summary>
    /// 人の画面に何も出さない起動か（<c>SPOITABLE_UI_OFFSCREEN</c>。e2e・使い捨ての起動）。窓は画面の外に置き、トレイにもアイコンを出さない
    /// （e2e は 8 つ同時にアプリを立てるので、出すと手元のトレイにアイコンが並ぶ）。
    /// </summary>
    public static bool OffscreenRequested => Environment.GetEnvironmentVariable("SPOITABLE_UI_OFFSCREEN") is { Length: > 0 };

    public DesktopWindow(UiBridge bridge, UiBroadcast broadcast)
    {
        _broadcast = broadcast;

        Text = "SpoitableToolKit";
        Icon = AppIcon.Window;

        // 枠は残して、上のキャプションだけ消す（WndProc の WM_NCCALCSIZE）。
        // FormBorderStyle.None にすると、リサイズも最大化もスナップも自前になる。枠を残しておけば、掴んで伸ばすのも Aero スナップも Windows がやってくれる —
        // しかもリサイズの縁は非クライアント領域なので、CEF の子窓に覆われない。
        ClientSize = new Size(1080, 720);
        MinimumSize = new Size(640, 400);
        StartPosition = FormStartPosition.CenterScreen;

        // e2e のときは画面の外へ置く。試験は本物の窓を実画面に出して動かすので、走っている間に人がその窓を閉じたり、フォーカスを奪ったり、触ったりすると落ちる。
        // 描画は止めない。最小化やサイズ 0 にすると windowed CEF が描くのをやめて、画面を見る試験が別の理由で落ちる。位置だけ画面の外へ動かす。
        _offscreen = OffscreenRequested;
        if (_offscreen)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            ShowInTaskbar = false;
        }

        _browser = new ChromiumWebBrowser($"{CefRuntime.UiOrigin}/index.html") { Dock = DockStyle.Fill };

        // bind はページのロードより前に済ませる必要がある。bind 自体は塞げない（ブラウザ単位なのでモジュールの iframe からも通る）。
        // 呼べるかどうかは token で分ける — token はシェルの HTML に埋め込んで配られる（SpoitableSchemeHandlerFactory）。
        _browser.JavascriptObjectRepository.Register("stkHost", bridge);

        // 既定のコンテキストメニューを止める。ブラウザ単位なので、これ 1 枚でモジュールの iframe まで止まる。自前のメニューはシェルが overlay で描く。
        _browser.MenuHandler = new ContextMenuBlocker();

        Controls.Add(_browser);
        _broadcast.Attach(_browser);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _browser.LoadingStateChanged += OnLoadingStateChanged;

        // 描画が始まる前に当てたい。間に合わなければ読み込み完了時にもう一度当てる。
        _browser.IsBrowserInitializedChanged += (_, _) => _ = CefTheme.ApplyAsync(_browser);
    }

    private void OnLoadingStateChanged(object? sender, LoadingStateChangedEventArgs e)
    {
        if (e.IsLoading) return;
        _browser.LoadingStateChanged -= OnLoadingStateChanged;

        _ = CefTheme.ApplyAsync(_browser);
    }

    private const int WmNcCalcSize = 0x0083;
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;

    private const int MonitorToNearest = 2;
    private const uint AbmGetState = 4;
    private const uint AbsAutoHide = 1;

    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);

    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")] private static extern IntPtr FindWindow(string? className, string? title);

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("shell32.dll")] private static extern uint SHAppBarMessage(uint message, ref AppBarData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public System.Drawing.Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size;
        public IntPtr Window;
        public uint CallbackMessage;
        public uint Edge;
        public Rect Bounds;
        public int LParam;
    }

    private enum TaskbarEdge
    {
        Left,
        Top,
        Right,
        Bottom,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>
    /// キャプションだけをクライアント領域に取り込む。既定の計算をさせてから上端だけ元に戻す。左右下は既定のまま残るので、リサイズの縁は非クライアント領域のまま = Windows がリサイズもスナップも面倒を見る（<c>FormBorderStyle.None</c> でやるとその全部が自前になる）。
    /// 最大化のときは縁の分だけ内側へ戻す。最大化した窓は画面より一回り大きく広がる作りなので、そのままだと上端と左右が画面の外へ出て切れる。
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WmGetMinMaxInfo:
                base.WndProc(ref m);
                LimitToWorkArea(m.LParam);
                return;

            case WmNcCalcSize when m.WParam != IntPtr.Zero:
                RemoveCaption(ref m);
                return;

            default:
                base.WndProc(ref m);
                return;
        }
    }

    /// <summary>画面の外に置いているか（e2e・使い捨ての起動）。その間は前に出ない。</summary>
    public bool Offscreen => _offscreen;

    /// <summary>
    /// 前に出る（最小化なら戻す）。画面の外に置いている間（e2e）は何もしない — 試験を回している間に、人が使っている窓からフォーカスを奪わない。
    /// 2 つ目に起動されたアプリは前に出す権利を先に渡してくれる（<see cref="SpoitableToolKit.Host.App.SingleInstance.AskTheRunningOneToShow"/>）。
    /// </summary>
    public void ComeToFront()
    {
        if (_offscreen) return;

        // 最小化なら元の大きさへ（最大化したまま最小化していたら、最大化に戻す — WindowState = Normal だと通常の大きさになる）。
        if (WindowState == FormWindowState.Minimized) ShowWindow(Handle, SwRestore);
        Activate();
        SetForegroundWindow(Handle);
    }

    /// <summary>
    /// 閉じるボタンの動きをシェルのモーダルで聞く。出せたら true（答えは <see cref="UiBridge.AnswerClose"/> で来る）。
    /// 出せたかどうかはページに聞く — 読み込み中・起動の途中（文言をまだ持っていない）なら、ページが false を返す。答えが返らない・JS を受け取れないときも false。
    /// 聞くのはこの窓のページだけ（VR の窓とダッシュボードも同じ broadcast にいるので、push では向こうにも出る）。
    /// </summary>
    public async Task<bool> AskToCloseAsync()
    {
        if (!_browser.CanExecuteJavascriptInMainFrame) return false;

        var response = await _browser.EvaluateScriptAsync(
            "window.stk && typeof window.stk.askClose === 'function' ? window.stk.askClose() : false", AskTimeout);
        return response.Success && response.Result is true;
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);

    /// <summary>
    /// 最大化の大きさを自分で決める。既定に任せると、枠の分だけ画面より一回り大きく広がる（普段は枠が隠すので見えない）。枠を消してあるとそのはみ出しがそのまま出て、上が切れる。
    /// 枠の厚みを引く方法は作業領域を見ていないので、タスクバーが上にある人や、自動的に隠す設定の人で合わない。作業領域へちょうど合わせれば引き算が要らない。
    /// </summary>
    private void LimitToWorkArea(IntPtr info)
    {
        var monitor = MonitorFromWindow(Handle, MonitorToNearest);
        if (monitor == IntPtr.Zero) return;

        var screen = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref screen)) return;

        var work = screen.Work;

        // **自動的に隠すタスクバーの辺は 1px 空ける。** 画面の端をこちらが覆い切ると、
        // マウスを端へ運んでもタスクバーが出てこられない。
        switch (AutoHideEdge(screen.Monitor))
        {
            case TaskbarEdge.Top: work.Top += 1; break;
            case TaskbarEdge.Bottom: work.Bottom -= 1; break;
            case TaskbarEdge.Left: work.Left += 1; break;
            case TaskbarEdge.Right: work.Right -= 1; break;
        }

        var value = Marshal.PtrToStructure<MinMaxInfo>(info);

        value.MaxPosition = new Point(work.Left - screen.Monitor.Left, work.Top - screen.Monitor.Top);
        value.MaxSize = new Point(work.Right - work.Left, work.Bottom - work.Top);

        Marshal.StructureToPtr(value, info, false);
    }

    /// <summary>
    /// 上のキャプションだけをクライアント領域に取り込む。左右下は既定のまま残すので、リサイズの縁は非クライアント領域のまま = Windows がリサイズもスナップも面倒を見る（CEF の子窓にも覆われない）。
    /// 最大化のときだけは縁も残さない。最大化した窓はリサイズできないので縁が要らないし、残すとその厚みが左右と下の隙間として出る。既定の計算を呼ばずに 0 を返すと、渡された矩形がそのままクライアント領域になる。
    /// 状態は <see cref="Form.WindowState"/> ではなく <c>IsZoomed</c> で見る。切り替えの途中はこのメッセージの方が先に来ることがあり、WinForms 側の状態はまだ古い（そこで判定を誤ると、一瞬だけ隙間が出る）。
    /// </summary>
    private void RemoveCaption(ref Message m)
    {
        var proposed = Marshal.PtrToStructure<Rect>(m.LParam);

        if (IsZoomed(Handle))
        {
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
        var calculated = Marshal.PtrToStructure<Rect>(m.LParam);

        calculated.Top = proposed.Top;

        Marshal.StructureToPtr(calculated, m.LParam, false);
        m.Result = IntPtr.Zero;
    }

    private static TaskbarEdge? AutoHideEdge(Rect monitor)
    {
        var data = new AppBarData { Size = Marshal.SizeOf<AppBarData>() };
        if ((SHAppBarMessage(AbmGetState, ref data) & AbsAutoHide) == 0) return null;

        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero || !GetWindowRect(tray, out var bounds)) return null;

        return bounds.Right - bounds.Left > bounds.Bottom - bounds.Top
            ? bounds.Top <= monitor.Top ? TaskbarEdge.Top : TaskbarEdge.Bottom
            : bounds.Left <= monitor.Left ? TaskbarEdge.Left : TaskbarEdge.Right;
    }

    /// <summary>どのスレッドから読んでもよい（状態を読むだけ）。</summary>
    public bool IsMaximized => WindowState == FormWindowState.Maximized;

    public void Minimize() => WindowState = FormWindowState.Minimized;

    public void ToggleMaximize()
    {
        WindowState = IsMaximized ? FormWindowState.Normal : FormWindowState.Maximized;

        // 枠の作り直しを頼む。頼まないと、最大化を解いたあと上が白・左右下が灰色の余白のまま残る（窓の大きさを変えると直る、という形で出る）。
        // 枠を自前で計算しているので、変わったことを言わないと WM_NCCALCSIZE が走らない。
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
            SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate);

        PerformLayout();
    }

    /// <summary>
    /// 掴んで動かす。マウスを追いかけない — 掴みを外してから「キャプションを押した」ことにして Windows へ渡すと、以降の追従は向こうがやる。
    /// <c>ReleaseCapture</c> が要るのは、押した時点で CEF の子窓がマウスを掴んでいるため。呼び出しは CEF の別スレッドから来るので、窓のスレッドへ移してから呼ばれる。
    /// </summary>
    public void BeginDrag()
    {
        // もう離されていたら掴まない。呼び出しは CEF から回ってくるので、ここへ届く頃にはボタンが上がっていることがある。
        // その状態で掴むと、押していないのに窓がマウスに付いてくる（掴みっぱなしに見える）。
        if ((MouseButtons & MouseButtons.Left) == 0) return;

        ReleaseCapture();
        SendMessage(Handle, WmNcLButtonDown, HtCaption, IntPtr.Zero);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _broadcast.Detach(_browser);
        base.OnFormClosed(e);
    }
}
