using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Tests.Helpers;

internal sealed class Logs : ILoggerFactory, ILogger
{
    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> Infos { get; } = [];

    public string[] All()
    {
        lock (Infos) lock (Warnings) lock (Errors) return [.. Infos, .. Warnings, .. Errors];
    }

    public List<string> Categories { get; } = [];

    public ILogger CreateLogger(string categoryName)
    {
        lock (Categories) Categories.Add(categoryName);
        return this;
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
        {
            lock (Warnings) Warnings.Add(formatter(state, exception));
            return;
        }

        if (logLevel == LogLevel.Information)
        {
            lock (Infos) Infos.Add(formatter(state, exception));
            return;
        }

        if (logLevel < LogLevel.Error) return;

        lock (Errors) Errors.Add(formatter(state, exception));
    }

    public void Dispose()
    {
    }
}
