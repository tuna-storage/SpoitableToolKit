using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Host.Settings;

namespace SpoitableToolKit.Host.AppLog;

/// <summary>
/// アプリログの記録を残す期間（<see cref="HostConfig.AppLogKeepDays"/>）。起動したときと設定を変えたときに古いものを消す。
/// 動いている間は日をまたいでも消さない（次に起動したときに消える）。
/// </summary>
internal sealed class AppLogRetention(HostConfig config, AppLogSink sink, Func<DateTimeOffset>? now = null) : IConfigEventListener<HostConfig>
{
    public DateTimeOffset Since =>
        (now?.Invoke() ?? DateTimeOffset.Now).AddDays(-Math.Clamp(config.AppLogKeepDays, MinDays, MaxDays));

    /// <summary>設定の範囲（<c>SpoitableSettingsRange</c>）と同じ。ここでも切る（<c>config.json</c> を手で 0 や負の数にされると今の行まで消える）。</summary>
    public const int MinDays = 1;

    public const int MaxDays = 365;

    /// <summary>古いものを消す。キューへ積むだけで待たない。</summary>
    public void Apply() => sink.Prune(Since);

    public void OnConfigChanged(ConfigChangedEventParams<HostConfig> e)
    {
        // 設定画面の「適用」は UI のスレッドから来る。積むだけなので待たない。
        if (e.Changed(c => c.AppLogKeepDays)) Apply();
    }
}
