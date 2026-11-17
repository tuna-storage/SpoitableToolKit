namespace SpoitableToolKit.Host.Ui.Push;

/// <summary>push（<c>configChanged</c>）の形。<c>language</c> が真なら、画面は辞書を取り直してパネルを読み込み直す。</summary>
internal sealed record ConfigChangedPush(bool Language)
{
    public const string Channel = "configChanged";
}
