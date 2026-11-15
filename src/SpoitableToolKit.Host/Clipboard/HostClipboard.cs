namespace SpoitableToolKit.Host.Clipboard;

/// <summary>
/// クリップボードの読み書き。
/// <para>
/// Win32 のクリップボードは STA スレッドからしか触れないが、呼び出し元は
/// CEF のスレッドだったり、将来はログ監視のスレッドだったりする。窓が開いているとも限らない
/// （通知から直接コピーする経路が来る）。そこで<b>使い捨ての STA スレッドを立てて</b>そこで書く。
/// UI スレッドに依存しないので、どこから呼んでも同じように動く。
/// </para>
/// </summary>
internal static class HostClipboard
{
    private static readonly Lock Gate = new();

    private static bool _inMemory;

    private static string? _memory;

    /// <summary>
    /// <b>Windows のクリップボードに触らず、アプリの中だけで持つ</b>（<c>SPOITABLE_CLIPBOARD=memory</c>。e2e が付ける）。
    /// <para>
    /// e2e は貼り付けを試すたびに本物のクリップボードへ書くと、<b>走らせている間の手元のコピーと取り合い</b>、
    /// 手元のクリップボードと履歴（Win+V）を荒らす。アプリごとに別々に持つので、e2e を並べても取り合わない。
    /// 切り替えるのは起動の最初の 1 回だけ。
    /// </para>
    /// </summary>
    public static void KeepInMemory() => _inMemory = true;

    public static bool SetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        if (_inMemory)
        {
            lock (Gate) _memory = text;
            return true;
        }

        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                // 他のプロセスが掴んでいるときのリトライは Clipboard.SetText が内部で持っている。
                System.Windows.Forms.Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return failure is null;
    }

    /// <summary>
    /// 読み出し。<b>書き込みと同じで使い捨ての STA スレッドを立てる</b>。
    /// <para>
    /// 取れない場合（文字が入っていない・他のプロセスが掴んでいる）は <c>null</c>。
    /// <b>例外を上げない</b>のは、貪り付けの失敗が右クリックそのものを壊す理由にはならないため。
    /// </para>
    /// </summary>
    public static string? GetText()
    {
        if (_inMemory)
        {
            lock (Gate) return _memory;
        }

        string? text = null;

        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Forms.Clipboard.ContainsText()) text = System.Windows.Forms.Clipboard.GetText();
            }
            catch (Exception ex)
            {
                // 掴まれているだけ。次に押されたときに取れればよい。**ただし跡は残す** — 貼り付けが黙って何もしないとき、
                // 読めなかったのか、空だったのかを見分けられない。
                Console.Error.WriteLine($"Could not read the clipboard: {ex.GetType().Name}: {ex.Message}");
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return text;
    }
}
