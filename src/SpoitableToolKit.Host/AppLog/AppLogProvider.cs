using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Host.AppLog;

/// <summary>
/// <c>ILogger</c> の出力を <see cref="AppLogSink"/> へ流す。<c>OutputType</c> が <c>WinExe</c> なので、
/// <c>AddSimpleConsole</c> の出力は配布した状態ではどこにも出ない。
/// レベルの足切りは <c>SetMinimumLevel</c>（<c>SPOITABLE_LOG_LEVEL</c>）が先に効くので、ここには通ったものだけが来る。
/// </summary>
internal sealed class AppLogProvider(AppLogSink sink) : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, AppLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new AppLogger(name, sink));

    public void Dispose() => _loggers.Clear();

    private sealed class AppLogger(string category, AppLogSink sink) : ILogger
    {
        /// <summary>スコープは持たない。<c>null</c> を返してはいけない（呼び手は <c>using</c> で包むので落ちる）。</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        /// <summary>足切りは <c>SetMinimumLevel</c> 側に任せる。ここで二重に持つと食い違う。</summary>
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            sink.Append(new AppLogEntry(
                DateTimeOffset.Now,
                logLevel,
                category,
                formatter(state, exception),
                exception?.ToString(),
                Environment.CurrentManagedThreadId));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
