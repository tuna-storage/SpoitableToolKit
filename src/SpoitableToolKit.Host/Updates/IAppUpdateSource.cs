using SpoitableToolKit.Host.Settings;

namespace SpoitableToolKit.Host.Updates;

/// <summary><b><see cref="IsInstalled"/> が false の間、<see cref="AppUpdates"/> はほかの口を呼ばない</b>（開発の bin と e2e は、どこにも繋がない）。</summary>
internal interface IAppUpdateSource
{
    bool IsInstalled { get; }

    string? InstalledVersion { get; }

    UpdateChannel? InstalledChannel { get; }

    Task<AppUpdateOffer?> CheckAsync(UpdateChannel channel, CancellationToken cancellationToken);

    /// <summary>落とす。<paramref name="progress"/> は 0〜100。</summary>
    Task DownloadAsync(AppUpdateOffer offer, Action<int> progress, CancellationToken cancellationToken);

    /// <summary>
    /// このアプリが閉じたら入れて起動し直すよう頼む。<b>true なら、呼んだ側がアプリを閉じる</b>
    /// （閉じるのはいつもの道 — 記録を書き切り、モジュールを畳んでから抜ける）。
    /// </summary>
    bool ApplyAfterExit(AppUpdateOffer offer);
}
