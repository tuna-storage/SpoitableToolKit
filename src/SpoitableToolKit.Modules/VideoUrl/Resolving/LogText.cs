namespace SpoitableToolKit.Modules.VideoUrl.Resolving;

/// <summary>ログと断り文句に出す前の整え。</summary>
internal static class LogText
{
    /// <summary>ログに出す前に長い出力を切る。yt-dlp のエラーは数十行になることがある。</summary>
    public static string Trim(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500] + "…";
    }
}
