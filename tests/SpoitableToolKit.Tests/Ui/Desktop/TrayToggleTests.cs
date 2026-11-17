using SpoitableToolKit.Host.Ui.Desktop;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Desktop;

/// <summary>トレイのアイコンの左クリックは「出ていればしまう、そうでなければ開く」。</summary>
public sealed class TrayToggleTests
{
    /// <summary>
    /// 出ている = 窓があって、表示されていて、最小化されていない。ほかの窓の後ろに隠れていても出ている（しまう）。
    /// しまっている（窓が無い）・最小化している・見えない窓は開く（最小化なら、1 回のクリックで元の大きさに戻る）。
    /// </summary>
    [Theory]
    [InlineData(false, false, false, "Open")]
    [InlineData(false, true, false, "Open")]
    [InlineData(true, true, false, "PutAway")]
    [InlineData(true, true, true, "Open")]
    [InlineData(true, false, false, "Open")]
    [InlineData(true, false, true, "Open")]
    public void AClickPutsAWindowOnScreenAwayAndOpensOtherwise(bool exists, bool visible, bool minimized, string expected)
    {
        Assert.Equal(Enum.Parse<TrayClick>(expected), TrayToggle.For(exists, visible, minimized));
        Assert.Equal(expected == "PutAway", TrayToggle.OnScreen(exists, visible, minimized));
    }
}
