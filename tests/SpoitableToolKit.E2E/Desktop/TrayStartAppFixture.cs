namespace SpoitableToolKit.E2E.Desktop;

/// <summary>Windows の起動時の起動と同じ引数（<c>--tray</c>）で立てるアプリ。窓を開かずに始まる（設定はしまって始めないのまま）。</summary>
public sealed class TrayStartAppFixture : AppFixture
{
    protected override IReadOnlyList<string> Arguments => ["--tray"];

    protected override bool StartsInTray => true;
}
