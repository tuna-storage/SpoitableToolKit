using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Host.VRChat;
using SpoitableToolKit.Host.VrcLog;

namespace SpoitableToolKit.Host.Status;

/// <summary>
/// タイトルバーの右の「VRChat のログを読んでいます」（状態の口の <c>host</c>）。Note は読んでいるファイル名。
/// <para>
/// 行ではなく、読むファイルが変わったときに書く（<see cref="Show"/> を <see cref="VrcLogBus.FileChanges"/> に繋ぐ — <see cref="StatusRouting"/>）。
/// 行に乗せると、VRChat が止まっている間は一度も出ない。
/// </para>
/// <para>
/// 作った時点で一度書く。監視は購読より先にファイルを見つけていることがあり、
/// ログが 1 つも無い人には「見つかりません」を出す機会がそこしかない。
/// </para>
/// <para>
/// VRChat が動いていなければ「VRChat は動いていません」。ログのファイルは閉じたあとも残るので、
/// 起動・終了のたびに <see cref="Show"/> し直す（<see cref="VRChatPresence.Connect"/>）。
/// </para>
/// </summary>
internal sealed class LogReadingStatus
{
    private readonly Func<string?> _currentFile;
    private readonly ILocalizer _text;
    private readonly Func<bool> _vrchatRunning;

    /// <param name="vrchatRunning">VRChat が動いているか。null なら動いているものとする（試験で、ログの側だけを見るとき）。</param>
    public LogReadingStatus(Func<string?> currentFile, ILocalizer text, Func<bool>? vrchatRunning = null)
    {
        _currentFile = currentFile;
        _text = text;
        _vrchatRunning = vrchatRunning ?? (() => true);
        Show();
    }

    public ModuleStatus Status { get; } = new();

    public string? FileName => _currentFile() is { } path ? Path.GetFileName(path) : null;

    /// <summary>書き直す。どのスレッドから呼んでもよい（同じ値なら Changed は上がらない）。言語を変えたあとにも呼ぶ。</summary>
    public void Show()
    {
        if (!_vrchatRunning())
        {
            Status.Text = _text["host.status.vrchat.off"];
            Status.Note = null;
            Status.State = StatusState.Off;
            return;
        }

        var file = FileName;
        Status.Text = _text[file is null ? "host.status.log.none" : "host.status.log.reading"];
        Status.Note = file;
        Status.State = file is null ? StatusState.Off : StatusState.Ok;
    }
}
