namespace SpoitableToolKit.Host.Modules.GitHub;

/// <summary>
/// その repo には公開されたリリースが無い（か、非公開・存在しない — GitHub の答えは同じ 404）。
/// 「ソースから build しますか」を出すかどうかを、<b>文ではなく型で</b>見分ける。
/// </summary>
internal sealed class NoReleaseException(string message) : InvalidOperationException(message);
