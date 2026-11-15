using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Updates;

namespace SpoitableToolKit.Tests.Updates;

internal sealed class FakeAppUpdateSource : IAppUpdateSource
{
    public bool IsInstalled { get; set; } = true;

    public string? InstalledVersion { get; set; } = "2026.11.0";

    public UpdateChannel? InstalledChannel { get; set; } = UpdateChannel.Stable;

    public AppUpdateOffer? Next { get; set; }

    public bool CheckFails { get; set; }

    public Exception? CheckThrows { get; set; }

    /// <summary>置けば、確かめはこれが済むまで答えない（確かめている最中に起きることを作る）。</summary>
    public TaskCompletionSource? CheckGate { get; set; }

    public bool DownloadFails { get; set; }

    /// <summary>頼んだあとアプリを閉じるか（本物は閉じる。e2e の形は閉じない）。</summary>
    public bool CloseAfterApply { get; set; } = true;

    public List<UpdateChannel> Checked { get; } = [];

    public List<string> Downloaded { get; } = [];

    public List<string> Applied { get; } = [];

    public async Task<AppUpdateOffer?> CheckAsync(UpdateChannel channel, CancellationToken cancellationToken)
    {
        lock (Checked) Checked.Add(channel);
        if (CheckGate is { } gate)
        {
            CheckGate = null;
            await gate.Task;
        }

        if (CheckThrows is { } thrown) throw thrown;
        if (CheckFails) throw new HttpRequestException("no answer");
        return Next;
    }

    public Task DownloadAsync(AppUpdateOffer offer, Action<int> progress, CancellationToken cancellationToken)
    {
        progress(40);
        if (DownloadFails) throw new IOException("the download broke off");
        progress(100);
        lock (Downloaded) Downloaded.Add(offer.Version);
        return Task.CompletedTask;
    }

    public bool ApplyAfterExit(AppUpdateOffer offer)
    {
        lock (Applied) Applied.Add(offer.Version);
        return CloseAfterApply;
    }

    public static AppUpdateOffer Offer(string version, bool downgrade = false, string notes = "- changed") => new(version, downgrade, notes);
}
