namespace SpoitableToolKit.Host.App.Startup;

/// <summary>
/// Windows の「スタートアップ アプリ」（タスクマネージャー・設定アプリ）での入/切。Run の値とは別の場所
/// （HKCU の <c>…\Explorer\StartupApproved\Run</c>、同じ名前の値）に Windows が書く。切られると Run の値は残ったまま、起動だけが止まる。
/// 読み方は <see cref="StartupRegistration.ApprovalOf"/>。
/// </summary>
internal enum StartupApproval
{
    /// <summary>印が無い（Windows は入として扱う）。</summary>
    Missing,

    Enabled,

    /// <summary>利用者が Windows の側で切った。</summary>
    Disabled,
}
