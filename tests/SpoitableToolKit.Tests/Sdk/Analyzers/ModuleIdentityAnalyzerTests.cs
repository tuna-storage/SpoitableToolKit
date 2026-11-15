using SpoitableToolKit.Sdk.Analyzers;
using Xunit;

namespace SpoitableToolKit.Tests.Sdk.Analyzers;

/// <summary>
/// STK040（<see cref="ModuleIdentityAnalyzer"/>）: モジュールのクラスに <c>Id</c>・<c>DisplayName</c> を手で書いたら止める。
/// この 2 つは csproj から dll に刻まれ、<c>ISpoitableModule</c> の既定の本体が返す。手で書くと csproj とずれて、本体が読み込まない。
/// </summary>
public sealed class ModuleIdentityAnalyzerTests
{
    private const string Usings = "using SpoitableToolKit.Abstractions;\n";

    private static async Task<string[]> FoundAsync(string source)
    {
        var found = await AnalyzerRun.RunAsync(new ModuleIdentityAnalyzer(), Usings + source);
        return [.. found.Select(AnalyzerRun.Describe)];
    }

    [Fact]
    public async Task AModuleThatLeavesThemToTheSdkIsFine()
    {
        var found = await FoundAsync("public sealed class P : ISpoitableModule { public bool OnInit(IModuleHost host) => true; }");

        Assert.Empty(found);
    }

    /// <summary>暗黙の実装（<c>public string Id =&gt; …</c>）は、書いた行ごとに 1 つ。文は直し方まで（AI が文だけで直せるように）。</summary>
    [Fact]
    public async Task WritingThemInTheClassIsAnError()
    {
        var found = await FoundAsync("""
            public sealed class P : ISpoitableModule
            {
                public bool OnInit(IModuleHost host) => true;
                public string Id => "p";
                public string DisplayName { get; } = "P";
            }
            """);

        Assert.Equal(
        [
            "STK040 5: P writes the module's Id by hand. Remove it: the SDK writes SpoitableModuleId and SpoitableModuleName from the csproj into the dll, " +
            "and ISpoitableModule.Id and ISpoitableModule.DisplayName already return them. " +
            "To change the Id or the name, change <SpoitableModuleId> or <SpoitableModuleName> in the csproj.",
            "STK040 6: P writes the module's DisplayName by hand. Remove it: the SDK writes SpoitableModuleId and SpoitableModuleName from the csproj into the dll, " +
            "and ISpoitableModule.Id and ISpoitableModule.DisplayName already return them. " +
            "To change the Id or the name, change <SpoitableModuleId> or <SpoitableModuleName> in the csproj.",
        ], found);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, ModuleIdentityAnalyzer.Rule.DefaultSeverity);
    }

    [Fact]
    public async Task WritingThemExplicitlyIsAnErrorToo()
    {
        var found = await FoundAsync("""
            public sealed class P : ISpoitableModule
            {
                public bool OnInit(IModuleHost host) => true;
                string ISpoitableModule.Id => "p";
            }
            """);

        Assert.Equal(["STK040 5: P writes the module's Id"], found.Select(f => f[..f.IndexOf(" by hand", StringComparison.Ordinal)]).ToArray());
    }

    /// <summary>
    /// 基底のクラスが書いたものは<b>基底の行に 1 回</b>（継いだモジュールの数だけ重ねない）。基底が ISpoitableModule を実装していなくても、
    /// 継いだモジュールの Id になるなら当たる。
    /// </summary>
    [Fact]
    public async Task WhatABaseClassWritesIsReportedOnceWhereItIsWritten()
    {
        var found = await FoundAsync("""
            public abstract class Base
            {
                public string Id => "shared";
            }
            public sealed class First : Base, ISpoitableModule { public bool OnInit(IModuleHost host) => true; }
            public sealed class Second : Base, ISpoitableModule { public bool OnInit(IModuleHost host) => true; }
            """);

        Assert.Equal(["STK040 4: Base writes the module's Id"], found.Select(f => f[..f.IndexOf(" by hand", StringComparison.Ordinal)]).ToArray());
    }

    /// <summary>
    /// ISpoitableModule を継いだ interface が既定の本体を書き直したもの。<b>それを実装したモジュールがあっても無くても</b>、書いた行に 1 回
    /// （無いのは、interface だけの dll。それを実装するモジュールは別の dll なので、そちらの build では、ソースに無いものとして見ない）。
    /// </summary>
    [Theory]
    [InlineData("public sealed class P : IMyModule { public bool OnInit(IModuleHost host) => true; }")]
    [InlineData("")]
    public async Task AnInterfaceThatRewritesTheDefaultIsReported(string implementation)
    {
        var found = await FoundAsync($$"""
            public interface IMyModule : ISpoitableModule
            {
                string ISpoitableModule.DisplayName => "Mine";
            }
            {{implementation}}
            """);

        Assert.Equal(["STK040 4: IMyModule writes the module's DisplayName"], found.Select(f => f[..f.IndexOf(" by hand", StringComparison.Ordinal)]).ToArray());
    }

    /// <summary>総称型の基底（<c>Base&lt;T&gt;</c>）が書いたものも、<b>書いた行に 1 回</b>（継いだクラスごとに <c>Base&lt;int&gt;.Id</c>・<c>Base&lt;string&gt;.Id</c> という別のものとして返る）。</summary>
    [Fact]
    public async Task WhatAGenericBaseWritesIsReportedOnceToo()
    {
        var found = await FoundAsync("""
            public abstract class Base<T> : ISpoitableModule
            {
                public bool OnInit(IModuleHost host) => true;
                public string Id => "shared";
            }
            public sealed class First : Base<int> { }
            public sealed class Second : Base<string> { }
            """);

        Assert.Equal(["STK040 5: Base writes the module's Id"], found.Select(f => f[..f.IndexOf(" by hand", StringComparison.Ordinal)]).ToArray());
    }

    /// <summary>
    /// <b>モジュールのクラス自身を手首のショートカットにして、<c>public string Id</c> がショートカットの Id も兼ねている</b>ときは、
    /// 「消す」ではなく「ショートカットの側を明示の実装にする」と言う（消すとショートカットが壊れ、書き戻すとまた当たる）。
    /// </summary>
    [Fact]
    public async Task AnIdThatIsAlsoAShortcutsIdIsToldToMoveToTheShortcut()
    {
        var found = await FoundAsync("""
            using System.Threading;
            using System.Threading.Tasks;
            using SpoitableToolKit.Abstractions.Shortcuts;
            public sealed class P : ISpoitableModule, IClickableWristShortcut
            {
                public bool OnInit(IModuleHost host) { host.Shortcuts.Register(this); return true; }
                public string Id => "go";
                public string Name => "Go";
                public Task OnTriggerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Assert.Equal(
        [
            "STK040 8: P.Id is also the module's Id, which the SDK writes from the csproj into the dll (ISpoitableModule.Id already returns it). " +
            "Keep it for IWristShortcut only by writing it explicitly (string IWristShortcut.Id => ...) instead of a public Id, so that ISpoitableModule.Id is left to the SDK. " +
            "To change the module's Id or name, change <SpoitableModuleId> or <SpoitableModuleName> in the csproj.",
        ], found);
    }

    [Fact]
    public async Task AnIdOnSomethingElseIsNotTheirBusiness()
    {
        var found = await FoundAsync("""
            using System.Threading;
            using System.Threading.Tasks;
            using SpoitableToolKit.Abstractions.Shortcuts;
            public sealed class Tile : IClickableWristShortcut
            {
                public string Id => "tile";
                public string Name => "Tile";
                public Task OnTriggerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }
            public sealed class Row { public string Id => "row"; public string DisplayName => "Row"; }
            """);

        Assert.Empty(found);
    }
}
