using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SpoitableToolKit.Host.App;

/// <summary>
/// アプリを置き場ごとに 1 つだけ動かす。
/// <para>
/// 2 つ動くと取り合うもの: 心拍計の GATT（片方が AccessDenied で 10 秒おきに繋ぎ直す）・VRChat への OSC（2 つから送る）・
/// 配信のポート・手首と空間の窓（SteamVR に 2 組）・config.json（あとから書いた方が勝つ）・ログの DB・展開中の .NET SDK。
/// </para>
/// <para>
/// 置き場（<see cref="AppPaths.DataDirectory"/>）のパスから名前を作るので、e2e・使い捨ての起動（<c>SPOITABLE_DATA_DIR</c>）は、
/// 利用者のアプリが動いていても止まらない。2 つ目は 1 つ目に「前に出て」と知らせて（<see cref="AskTheRunningOneToShow"/>）すぐ終わる。
/// </para>
/// <para>
/// 知らせは置き場ごとの名前付きの印（<see cref="ShowRequests"/>）。立てた印は、1 つ目が受け取るまで立ったまま残るので、
/// 1 つ目がまだ待ち始めていない間（起動の数秒）に来た知らせも、待ち始めてから受け取れる。
/// </para>
/// <para>
/// 起動の頭で取り、アプリの最後（記録を書き切ったあと）で離す。「再起動」で立った新しいアプリは、前のアプリが離すまで待つ
/// （<see cref="AppRestart.WaitArgument"/>）。前のアプリが落ちて離さなかったときも、次のアプリは取れる（<see cref="PreviousEndedAbruptly"/>）。
/// </para>
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    /// <summary>もう動いていたので、窓を前に出してもらって終わった、の終了コード。</summary>
    public const int AlreadyRunningExitCode = 3;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showRequests;
    private bool _released;

    private SingleInstance(Mutex mutex, string dataDirectory, bool previousEndedAbruptly)
    {
        _mutex = mutex;
        _showRequests = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ShowEventNameFor(dataDirectory));
        PreviousEndedAbruptly = previousEndedAbruptly;
    }

    /// <summary>前のアプリが、離さずに終わっていた（落ちた・止められた）。取れてはいる。</summary>
    public bool PreviousEndedAbruptly { get; }

    /// <summary>
    /// 2 つ目に起動されたアプリが「前に出て」と言ったら立つ印（受け取ると自分で戻る）。受け取るまで立ったまま残る。
    /// デスクトップの側（<see cref="SpoitableToolKit.Host.Ui.Desktop.DesktopApp"/>）が待つ。窓をトレイにしまっていれば、作って開く。
    /// </summary>
    public WaitHandle ShowRequests => _showRequests;

    /// <summary>
    /// 置き場ごとの印（パスの SHA-256 の頭 16 バイト）。大文字小文字と、終わりの区切りは見ない（Windows では同じフォルダ）。
    /// </summary>
    internal static string KeyFor(string dataDirectory)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory)).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)), 0, 16);
    }

    /// <summary>Mutex の名前（ログオンしている利用者の中だけで見える <c>Local\</c>）。</summary>
    internal static string MutexNameFor(string dataDirectory) => @"Local\SpoitableToolKit." + KeyFor(dataDirectory);

    /// <summary>「前に出て」の印の名前。置き場ごと（e2e のアプリが、利用者の窓を前に出さない）。</summary>
    internal static string ShowEventNameFor(string dataDirectory) => @"Local\SpoitableToolKit.Show." + KeyFor(dataDirectory);

    /// <summary>
    /// 取る。<paramref name="wait"/> まで待つ（ふつうの起動は待たない。「再起動」は前のアプリが離すまで待つ）。取れなければ null。
    /// 取ったスレッドが持ち主（離すのも同じスレッドから — <see cref="Dispose"/>）。
    /// </summary>
    public static SingleInstance? TryAcquire(string dataDirectory, TimeSpan wait)
    {
        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: false, MutexNameFor(dataDirectory));
        }
        catch (UnauthorizedAccessException)
        {
            // 1 つ目が管理者として動いていると、ふつうの権限ではその印を開けない。もう動いている。
            return null;
        }

        try
        {
            if (mutex.WaitOne(wait)) return new SingleInstance(mutex, dataDirectory, previousEndedAbruptly: false);
        }
        catch (AbandonedMutexException)
        {
            // 前のアプリが離さずに終わった。取れている（持ち主はこのスレッド）。
            return new SingleInstance(mutex, dataDirectory, previousEndedAbruptly: true);
        }

        mutex.Dispose();
        return null;
    }

    /// <summary>
    /// もう動いているアプリに、窓を前に出してもらう（印を立てるだけ。届いたかは分からない）。
    /// いま起動した側は前に出せるが、裏で動いている 1 つ目は自分では出られないので、その権利を先に渡す。
    /// </summary>
    public static void AskTheRunningOneToShow(string dataDirectory)
    {
        AllowSetForegroundWindow(AsfwAny);

        try
        {
            if (!EventWaitHandle.TryOpenExisting(ShowEventNameFor(dataDirectory), out var show)) return;
            using (show) show.Set();
        }
        catch (UnauthorizedAccessException)
        {
            // 1 つ目が管理者として動いている。前には出せない。
        }
    }

    /// <summary>離す。取ったスレッドから呼ぶ（Mutex の決まり）。何度呼んでもよい。</summary>
    public void Dispose()
    {
        if (_released) return;
        _released = true;

        _showRequests.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    private const int AsfwAny = -1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
