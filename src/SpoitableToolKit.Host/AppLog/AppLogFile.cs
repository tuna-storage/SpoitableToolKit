using System.Globalization;
using SpoitableToolKit.Host.VrcLog.Store;

namespace SpoitableToolKit.Host.AppLog;

/// <summary>
/// アプリログの書き出し先。起動ごとに 1 本作り、古いものを間引く。
/// <para>
/// 1 行ごとに flush する。<see cref="SqliteVrcLogStore"/> のように Channel でまとめて流すと強制終了で末尾が飛び、
/// 追記ファイルを選んだ理由（落ちたときに残る）を潰す。アプリログは量が少ないので毎行でも困らない。
/// </para>
/// <para>
/// ここは例外を投げない。書き出しの失敗でアプリが落ちないよう、一度失敗したら黙って書くのをやめる
/// （<c>ILogger</c> で報告すると自分を呼び直すので、報告は <see cref="Console.Error"/> へ 1 回だけ）。
/// </para>
/// </summary>
internal sealed class AppLogFile : IDisposable
{
    /// <remarks><c>:</c> は Windows のファイル名に使えないので、時刻も <c>-</c> 区切りにする。</remarks>
    public const string NameFormat = "yyyy-MM-dd_HH-mm-ss";

    private readonly Lock _gate = new();
    private StreamWriter? _writer;
    private bool _broken;

    public string? Path { get; }

    /// <param name="keep">残す本数（新しい方から。今作る 1 本を含む）。</param>
    public AppLogFile(string directory, int keep, DateTimeOffset now)
    {
        try
        {
            Directory.CreateDirectory(directory);
            Path = Create(directory, now);
            _writer = new StreamWriter(Path, append: true) { AutoFlush = true };

            // 新しい 1 本を作ってから間引く（先に間引くと keep + 1 本になる）。
            Prune(directory, keep);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    /// <summary>名前が埋まっていたら連番を足す。同じ秒に 2 つ起動して同じファイルへ書くと行が混ざるので、必ず別の名前にする。</summary>
    private static string Create(string directory, DateTimeOffset now)
    {
        var stem = now.ToString(NameFormat, CultureInfo.InvariantCulture);
        var candidate = System.IO.Path.Combine(directory, $"{stem}.log");

        for (var suffix = 2; File.Exists(candidate); suffix++)
            candidate = System.IO.Path.Combine(directory, $"{stem}_{suffix}.log");

        return candidate;
    }

    /// <summary>古い方から消す。消せなくても続ける（他のプロセスが掴んでいることがある）。</summary>
    private static void Prune(string directory, int keep)
    {
        if (keep <= 0) return;

        var stale = Directory.GetFiles(directory, "*.log")
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .Skip(keep);

        foreach (var path in stale)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>1 行書く。例外はそのあとの行に、字下げして丸ごと。</summary>
    public void Write(AppLogEntry entry)
    {
        lock (_gate)
        {
            if (_writer is null) return;

            try
            {
                // 画面と同じ形にミリ秒だけ足す（競合を追うのに差が効く）。
                _writer.Write(entry.Timestamp.ToString("yyyy/MM/dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                _writer.Write(' ');
                _writer.Write(entry.Short);
                _writer.Write(" #");
                _writer.Write(entry.ThreadId.ToString(CultureInfo.InvariantCulture));
                _writer.Write(' ');
                _writer.Write(entry.Category);
                _writer.Write("  ");
                _writer.WriteLine(entry.Message);

                if (entry.Exception is { Length: > 0 } exception)
                {
                    // スタックトレースは複数行のまま残す（畳むと読めない）。
                    foreach (var line in exception.Split('\n'))
                        _writer.WriteLine("    " + line.TrimEnd('\r'));
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }
    }

    private void Fail(Exception ex)
    {
        _writer?.Dispose();
        _writer = null;

        if (_broken) return;
        _broken = true;

        Console.Error.WriteLine($"Cannot write the app log any more: {ex.Message}");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
