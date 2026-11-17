using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Shell;

/// <summary>
/// 起動の頭で、設定の言語を読んでから辞書を読む。読む前に辞書を読むと OS の言語になり、モジュールの読み込みの間に引く文言までその言語で残る
/// （読み込みのあとに辞書を読み直しても、もう字にして持っているものは変わらない）。
/// 見るのは、読み込めなかったモジュール（AppFixture が置く broken）の帯の題名。読み込みの途中で、本体の辞書から引いて字にしている。
/// </summary>
public sealed class StartupLanguageTests(OtherLanguageAppFixture app) : IClassFixture<OtherLanguageAppFixture>
{
    private IPage Page => app.Page;

    /// <summary>その言語の辞書の、その文言（アプリに同梱のものを読む）。</summary>
    private static string Dictionary(string language, string key) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppFixture.HostDirectory, "locales", language + ".json")))![key]!.GetValue<string>();

    [Fact]
    public async Task WhatModulesLoadingSaysIsInTheConfiguredLanguageEvenWhenTheOsSpeaksAnother()
    {
        var expected = string.Format(CultureInfo.InvariantCulture, Dictionary(OtherLanguageAppFixture.Configured, "host.module.failed"), "broken");
        var other = string.Format(CultureInfo.InvariantCulture, Dictionary(OtherLanguageAppFixture.OsLanguage, "host.module.failed"), "broken");
        Assert.NotEqual(expected, other);

        var banner = Page.Locator("#banners .banner").Filter(new() { HasTextString = "broken" });
        await Assertions.Expect(banner).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Assertions.Expect(banner.Locator(".title")).ToHaveTextAsync(expected);
    }
}
