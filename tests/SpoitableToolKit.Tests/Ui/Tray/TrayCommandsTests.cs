using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Tray;
using SpoitableToolKit.Host.Ui.Vr;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Tray;

/// <summary>
/// トレイのメニューは Windows 標準のメニューで出し、押された項目は番号で返ってくる。その番号を振る所と、項目へ戻す所（Win32 を通さない部分）。
/// </summary>
public sealed class TrayCommandsTests
{
    private static IReadOnlyList<TrayMenuItem> Menu() => TrayMenu.Build(new FakeLocalizer(), VrFeatures.Of(new HostConfig()));

    /// <summary>押せる項目にだけ、上から 1 から振る。サブメニューは親の位置で潜る（開く → VR オーバーレイ → SteamVR ダッシュボード → 終了）。</summary>
    [Fact]
    public void ThePressableItemsAreNumberedFromOneInOrder()
    {
        var commands = TrayCommands.For(Menu());

        Assert.Equal(
            [TrayMenu.Open, TrayMenu.VrOverlay, TrayMenu.VrDashboard, TrayMenu.Exit],
            Enumerable.Range(1, 4).Select(commands.IdOf));
        Assert.Null(commands.IdOf(5));
    }

    /// <summary>0 は「何も押さなかった」（外を押した・Esc）。振っていない番号もどの項目にも当たらない。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99)]
    public void NothingPressedOrAnUnknownNumberIsNoItem(int command)
    {
        Assert.Null(TrayCommands.For(Menu()).IdOf(command));
    }

    /// <summary>区切りとサブメニューの親には振らない（押しても何もしない所）。振った番号は、組み立てに使った項目から引ける。</summary>
    [Fact]
    public void SeparatorsAndSubmenuParentsGetNoNumber()
    {
        var items = Menu();
        var commands = TrayCommands.For(items);

        Assert.Equal(0, commands.NumberOf(items.Single(item => item.Id == TrayMenu.Vr)));
        Assert.Equal(0, commands.NumberOf(items.Single(item => item.Separator)));
        Assert.Equal(1, commands.NumberOf(items.Single(item => item.Id == TrayMenu.Open)));
        Assert.Equal(4, commands.NumberOf(items.Single(item => item.Id == TrayMenu.Exit)));

        var dashboard = items.Single(item => item.Id == TrayMenu.Vr).Children.Single(child => child.Id == TrayMenu.VrDashboard);
        Assert.Equal(3, commands.NumberOf(dashboard));
        Assert.Equal(TrayMenu.VrDashboard, commands.IdOf(commands.NumberOf(dashboard)));
    }

    /// <summary>番号は組み立てたメニューごと（開くたびに振り直す）。ほかのメニューの項目には 0 を返す。</summary>
    [Fact]
    public void AnItemOfAnotherMenuHasNoNumber()
    {
        var commands = TrayCommands.For(Menu());

        Assert.Equal(0, commands.NumberOf(Menu()[0]));
    }

    /// <summary>uxtheme の序数 135・136 が今の意味になったのは 1809（17763）から。それより前では引かない（メニューは明るいまま）。</summary>
    [Theory]
    [InlineData(17134, false)]
    [InlineData(17763, true)]
    [InlineData(18362, true)]
    [InlineData(26100, true)]
    public void DarkMenusAreOnlyAskedForWhereTheOrdinalsMeanThat(int build, bool expected)
    {
        Assert.Equal(expected, DarkMenus.CanUseOn(build));
    }
}
