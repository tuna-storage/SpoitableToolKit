using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Vr;

/// <summary>
/// 手首のタイルの 2 つの種類を、シェルの本物の bridge から本物の台帳とモジュールまで通す。
/// 手首のページが送るのと同じ知らせ（押すだけのタイルは <c>pressShortcut</c>、ドラッグだけのタイルは <c>shortcutPointer</c>）を送る。
/// <list type="bullet">
/// <item>押すだけ: 動画 URL の <c>videourl/resolve-latest</c>。解ける失敗がまだ無ければ「解くものが無い」トーストを出す（e2e のアプリには失敗が無い）。</item>
/// <item>ドラッグだけ: 本物のモジュール <c>sample</c> の <c>sample/knob</c>（<see cref="SampleAppFixture"/> が入れておく）。始めと終わりの位置をトーストに出す。</item>
/// </list>
/// 番号はアプリごとに数えるので、このクラスにアプリを 1 つ持つ。
/// </summary>
public sealed class WristGestureTests(SampleAppFixture app) : IClassFixture<SampleAppFixture>
{
    private const string Press = "videourl/resolve-latest";

    private const string Knob = "sample/knob";

    private IPage Page => app.Page;

    /// <summary>
    /// 押すだけのタイルへ送った押した・離したは何もしない（押しは click の口で来る）。
    /// 最後に押すだけのタイルを押しの口で押すとトーストが出る。ここまでのどれもがトーストを出していないことを、ここで締める。
    /// </summary>
    [Fact]
    public async Task ADraggableTileDragsFromThePressAndAClickableTileOnlyTakesThePress()
    {
        var none = await Page.EvaluateAsync<string>("() => t('videourl.shortcut.resolveLatest.none')");
        var pressed = Page.Locator("#toasts .toast").Filter(new() { HasText = none });
        var started = Page.Locator("#toasts .toast").Filter(new() { HasText = "knob start" });
        var ended = Page.Locator("#toasts .toast").Filter(new() { HasText = "knob end" });

        // ドラッグだけのタイル: 押した瞬間に始まる（離す前に、始めのトーストが出る）。
        await Pointer(Knob, 1, "down", 28, 24);
        await Assertions.Expect(started).ToHaveCountAsync(1);
        await Assertions.Expect(started).ToContainTextAsync("0.25,0.25 from 0.25,0.25");

        await Pointer(Knob, 1, "move", 56, 48);
        await Pointer(Knob, 1, "up", 84, 72);
        await Assertions.Expect(ended).ToHaveCountAsync(1);
        await Assertions.Expect(ended).ToContainTextAsync("0.75,0.75 from 0.25,0.25");

        // 種類の違う口は何もしない。
        await Pointer(Press, 2, "down", 50, 50);
        await Pointer(Press, 2, "up", 50, 50);
        await Page.EvaluateAsync($"() => bridge.pressShortcut('{Knob}')");

        // 出ないことを見るので、少し待つ（出るなら、どれもすぐ出る — 解く失敗が無く、ドラッグの口もすぐ返る）。
        await Task.Delay(300);
        Assert.Equal(0, await pressed.CountAsync());
        Assert.Equal(1, await started.CountAsync());

        // 押すだけのタイルを押しの口で押す。
        await Page.EvaluateAsync($"() => bridge.pressShortcut('{Press}')");
        await Assertions.Expect(pressed).ToHaveCountAsync(1);
        Assert.Equal(1, await started.CountAsync());
        Assert.Equal(1, await ended.CountAsync());
    }

    private Task Pointer(string key, long seq, string kind, double x, double y) =>
        Page.EvaluateAsync($"() => bridge.shortcutPointer('e2e', '{key}', {seq}, '{kind}', {x}, {y}, 112, 96)");
}
