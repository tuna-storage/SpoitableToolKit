namespace SpoitableToolKit.Host.Ui.Desktop;

/// <summary>
/// 窓のアイコン（タイトルバー・タスクバー）。exe のアイコンと同じ .ico を dll に埋めたもの — WinForms の窓は exe のアイコンを使わず、既定の絵を出す。
/// 1 つを使い回す（Form は渡された Icon を片付けないので、開くたびに作ると窓の数だけ残る）。
/// </summary>
internal static class AppIcon
{
    internal const string ResourceName = "SpoitableToolKit.Host.app-icon.ico";

    private static readonly Lazy<Icon> Shared = new(Load);

    public static Icon Window => Shared.Value;

    /// <summary>.ico の中身ごと持たせる。小さい方（タイトルバー）は WinForms がこの中から合う大きさを選び直す。</summary>
    internal static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is not embedded in the dll (see EmbeddedResource in SpoitableToolKit.Host.csproj)");
        return new Icon(stream);
    }
}
