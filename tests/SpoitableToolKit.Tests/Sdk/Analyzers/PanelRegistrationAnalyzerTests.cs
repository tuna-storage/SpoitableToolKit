using Microsoft.CodeAnalysis;
using SpoitableToolKit.Sdk.Analyzers;
using Xunit;

namespace SpoitableToolKit.Tests.Sdk.Analyzers;

/// <summary>
/// STK041（<see cref="PanelRegistrationAnalyzer"/>）: <c>web/index.html</c> があるのに <c>host.Ui.RegisterPanel</c> を呼んでいなければ警告。
/// 本体は登録したパネルだけを出すので、登録し忘れると「パネルを作ったのに出ない」だけが残る。<c>web/index.html</c> の有無は SDK の targets が渡す。
/// </summary>
public sealed class PanelRegistrationAnalyzerTests
{
    private const string Message =
        "This module has web/index.html but never calls host.Ui.RegisterPanel, so its panel is not shown in the sidebar. " +
        "Call host.Ui.RegisterPanel(\"index.html\") in OnInit, or remove web/index.html if the module has no panel.";

    private const string Usings = "using SpoitableToolKit.Abstractions;\n";

    private static async Task<string[]> FoundAsync(string source, bool hasPanelPage = true)
    {
        var found = await AnalyzerRun.RunAsync(new PanelRegistrationAnalyzer(), Usings + source, hasPanelPage);
        return [.. found.Select(AnalyzerRun.Describe)];
    }

    [Fact]
    public async Task APageThatIsNeverRegisteredIsWarnedAboutOnTheModuleClass()
    {
        var found = await FoundAsync("""
            public sealed class P : ISpoitableModule
            {
                public bool OnInit(IModuleHost host) => true;
            }
            """);

        Assert.Equal([$"STK041 2: {Message}"], found);
        Assert.Equal(DiagnosticSeverity.Warning, PanelRegistrationAnalyzer.Rule.DefaultSeverity);
    }

    /// <summary>呼び出しがあれば何も言わない。<c>if</c> の中・別のメソッドの中・メソッドとして渡したものも、呼んだことにする（どのページかは見ない）。</summary>
    /// <param name="call">OnInit に書く行。</param>
    /// <param name="helper">クラスに足すメソッド（無ければ空）。<b>その場合にだけ入れる</b> — どの場合にも入れると、どれもその中の呼び出しで通ってしまう。</param>
    [Theory]
    [InlineData("host.Ui.RegisterPanel(\"index.html\");", "")]
    [InlineData("if (host.DataDirectory.Length > 0) host.Ui.RegisterPanel(\"other.html\");", "")]
    [InlineData("Register(host.Ui);", "private static void Register(SpoitableToolKit.Abstractions.Ui.IModuleUi ui) => ui.RegisterPanel(\"index.html\");")]
    [InlineData("System.Action<string> register = host.Ui.RegisterPanel; register(\"index.html\");", "")]
    public async Task ACallAnywhereCounts(string call, string helper)
    {
        var found = await FoundAsync($$"""
            public sealed class P : ISpoitableModule
            {
                public bool OnInit(IModuleHost host)
                {
                    {{call}}
                    return true;
                }

                {{helper}}
            }
            """);

        Assert.Empty(found);
    }

    [Fact]
    public async Task WithoutThePageNothingIsSaid()
    {
        var found = await FoundAsync("public sealed class P : ISpoitableModule { public bool OnInit(IModuleHost host) => true; }", hasPanelPage: false);

        Assert.Empty(found);
    }

    /// <summary>
    /// モジュールのクラスが 2 つなら、名前の順で最初のものに 1 回（毎回同じ所に出る）。抽象のクラスには出さない
    /// （抽象の基底 <c>Abstract</c> は名前の順では先頭だが、そこには出ない）。
    /// </summary>
    [Fact]
    public async Task TwoModuleClassesGetOneWarningOnTheFirstByName()
    {
        var found = await FoundAsync("""
            public abstract class Abstract : ISpoitableModule { public abstract bool OnInit(IModuleHost host); }
            public sealed class Zed : Abstract { public override bool OnInit(IModuleHost host) => true; }
            public sealed class Alpha : Abstract { public override bool OnInit(IModuleHost host) => true; }
            """);

        Assert.Equal([$"STK041 4: {Message}"], found);
    }

    [Fact]
    public async Task WithoutAModuleClassTheWarningHasNoPlace()
    {
        var found = await FoundAsync("public static class Nothing { }");

        Assert.Equal([$"STK041 0: {Message}"], found);
    }
}
