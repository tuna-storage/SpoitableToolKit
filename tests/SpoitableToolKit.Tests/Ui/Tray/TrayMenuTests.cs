using SpoitableToolKit.Host.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Ui.Tray;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Tray;

/// <summary>トレイの右クリックのメニュー。中身は WinForms を通さずに組み立てるので、ここで見られる。</summary>
public sealed class TrayMenuTests
{
    private static string[] Shape(IReadOnlyList<TrayMenuItem> items) =>
        [.. items.Select(item => item.Separator ? "---" : $"{item.Id}:{item.Label}")];

    [Fact]
    public void TheMenuOpensTheWindowAndExits()
    {
        var items = TrayMenu.Build(new FakeLocalizer());

        Assert.Equal(["open:host.tray.open", "---", "exit:host.tray.exit"], Shape(items));
    }

    /// <summary>左クリックと同じ「開く」を太字にする（Windows のトレイのメニューの決まり）。</summary>
    [Fact]
    public void OpenIsTheDefaultItem()
    {
        var items = TrayMenu.Build(new FakeLocalizer());

        Assert.Equal([TrayMenu.Open], items.Where(item => item.Default).Select(item => item.Id));
        Assert.All(items, item => Assert.True(item.Enabled));
    }

    /// <summary>
    /// 開くたびに組み立て直すので、言語を変えると次に開いたメニューから変わる（起動し直さなくてよい）。
    /// 同じ辞書を読み直したあとに組むと、新しい言語の字になっている。
    /// </summary>
    [Fact]
    public void BuildingAgainAfterTheLanguageChangesUsesTheNewLanguage()
    {
        var text = new Localizer(NullLogger<Localizer>.Instance);

        text.Load("ja", [LocaleSource.Host]);
        var japanese = Shape(TrayMenu.Build(text));
        text.Load("en", [LocaleSource.Host]);
        var english = Shape(TrayMenu.Build(text));

        Assert.Equal(["open:開く", "---", "exit:終了"], japanese);
        Assert.Equal(["open:Open", "---", "exit:Exit"], english);
    }
}
