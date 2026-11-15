using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Host.AppLog;

/// <summary>
/// アプリログの受け口の中で起きたことを書く先。ILogger の受け口へは書けない（書くとこの受け口へ戻ってきて、入れ子を止める印で捨てられる）ので、
/// 標準エラーへ。書くのは警告より上だけ。
/// </summary>
/// <param name="to">書く先。null なら、書くときの標準エラー（<see cref="Console.Error"/>）。</param>
internal sealed class StandardErrorLogger(TextWriter? to = null) : ILogger
{
    public static readonly StandardErrorLogger Instance = new();

    /// <summary>scope は持たない。null を返すと、<c>using</c> で包む呼び手が落ちる（<c>AppLogProvider</c> と同じ）。</summary>
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        (to ?? Console.Error).WriteLine(exception is null ? $"[app log] {message}" : $"[app log] {message}: {exception}");
    }

    private sealed class NoScope : IDisposable
    {
        public static readonly NoScope Instance = new();

        public void Dispose()
        {
        }
    }
}
