using Microsoft.Playwright;

namespace SpoitableToolKit.E2E;

/// <summary>ネイティブの &lt;select&gt; は VR の面に乗らないので隠してあり、隠れた要素は Playwright も触れない。実際に見えている被せた側を押す。開いた一覧・暦は body に fixed で置かれる（囲いの overflow: hidden で切られないため）ので、開いている 1 つを掴む。</summary>
internal static class InPage
{
    private static ILocator OpenList(IPage page) => page.Locator(".spoitable-pick-list:not([hidden])");

    private static ILocator OpenWhen(IPage page) => page.Locator(".spoitable-when-pop:not([hidden])");

    public static async Task PickAsync(IPage page, string select, string value)
    {
        await page.Locator($".spoitable-pick-box:has({select}) .spoitable-pick-value").ClickAsync();
        await OpenList(page).Locator($"button[data-value='{value}']").ClickAsync();
    }

    public static async Task OpenWhenAsync(IPage page, string input) =>
        await page.Locator($".spoitable-when-box:has({input}) .spoitable-when-value").ClickAsync();

    public static async Task WhenNowAsync(IPage page) =>
        await OpenWhen(page).Locator(".spoitable-when-foot button").First.ClickAsync();

    public static async Task WhenClearAsync(IPage page) =>
        await OpenWhen(page).Locator(".spoitable-when-foot button").Last.ClickAsync();

    public static async Task WhenPreviousMonthAsync(IPage page) =>
        await OpenWhen(page).Locator(".spoitable-when-head button").First.ClickAsync();

    /// <summary>一覧は暦の中に居ない（時・分も pick.js の部品で body へ出る）。</summary>
    public static async Task WhenHourAsync(IPage page, int hour)
    {
        await OpenWhen(page).Locator(".spoitable-when-time .spoitable-pick-box").First.Locator(".spoitable-pick-value").ClickAsync();
        await OpenList(page).Locator($"button[data-value='{hour}']").ClickAsync();
    }

    public static async Task WhenDayAsync(IPage page, int day) =>
        await OpenWhen(page).Locator(".spoitable-when-grid button:not(.spoitable-when-other)")
            .Filter(new() { HasTextString = day.ToString() })
            .First.ClickAsync();
}
