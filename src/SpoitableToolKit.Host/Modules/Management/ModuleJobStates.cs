namespace SpoitableToolKit.Host.Modules.Management;

/// <summary>1 件の状態（画面へは、この名前のまま渡す）。</summary>
internal static class ModuleJobStates
{
    public const string Looking = "looking";
    public const string Downloading = "downloading";
    public const string AskBuild = "askBuild";
    public const string AskSdk = "askSdk";
    public const string SdkDownloading = "sdkDownloading";
    public const string SdkInstalling = "sdkInstalling";
    public const string Queued = "queued";
    public const string Building = "building";
    public const string AskInstall = "askInstall";
    public const string Failed = "failed";
}
