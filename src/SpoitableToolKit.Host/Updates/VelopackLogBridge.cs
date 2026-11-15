using Microsoft.Extensions.Logging;
using Velopack.Logging;

namespace SpoitableToolKit.Host.Updates;

/// <summary>Velopack の記録をアプリのログへ流す（確かめ・ダウンロードが失敗したときの理由を、利用者のログに残すため）。</summary>
internal sealed class VelopackLogBridge(ILogger logger) : IVelopackLogger
{
    public void Log(VelopackLogLevel logLevel, string? message, Exception? exception) =>
        logger.Log(logLevel switch
        {
            VelopackLogLevel.Trace => LogLevel.Trace,
            VelopackLogLevel.Debug => LogLevel.Debug,
            VelopackLogLevel.Warning => LogLevel.Warning,
            VelopackLogLevel.Error => LogLevel.Error,
            VelopackLogLevel.Critical => LogLevel.Critical,
            _ => LogLevel.Information,
        }, exception, "Velopack: {Message}", message);
}
