using System.Globalization;

namespace SpoitableToolKit.E2E.Shell;

/// <summary>
/// 設定の言語を、OS の言語（設定が Auto のときにアプリが選ぶ言語）と逆にして立てるアプリ。ja と en のどちらの PC でも、設定と OS が食い違う形になる。
/// アプリが Auto で選ぶのは、OS の表示言語が ja か en ならそれ、ほかは en（Localizer と同じ決まり）。
/// </summary>
public sealed class OtherLanguageAppFixture : AppFixture
{
    /// <summary>OS の言語で選ばれる方（ja か en）。</summary>
    public static string OsLanguage => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "ja" ? "ja" : "en";

    /// <summary>設定に書く方（OS の言語の逆）。</summary>
    public static string Configured => OsLanguage == "ja" ? "en" : "ja";

    protected override void Prepare()
    {
        var name = Configured == "ja" ? "Ja" : "En";
        File.WriteAllText(Path.Combine(DataDirectory, "config.json"), $$"""{ "Language": "{{name}}" }""");
    }
}
