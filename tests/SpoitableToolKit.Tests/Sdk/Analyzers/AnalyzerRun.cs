using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SpoitableToolKit.Abstractions;
using Xunit;

namespace SpoitableToolKit.Tests.Sdk.Analyzers;

/// <summary>断片そのものがコンパイルできることも確かめる（型が引けないと、アナライザーは何も言わずに通る）。</summary>
internal static class AnalyzerRun
{
    private static readonly MetadataReference[] References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is var name && (name.StartsWith("System.", StringComparison.Ordinal) || name is "netstandard.dll" or "mscorlib.dll"))
            .Select(path => MetadataReference.CreateFromFile(path)),
        MetadataReference.CreateFromFile(typeof(ISpoitableModule).Assembly.Location),
    ];

    public static async Task<IReadOnlyList<Diagnostic>> RunAsync(DiagnosticAnalyzer analyzer, string source, bool hasPanelPage = false)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), path: "Module.cs");
        var compilation = CSharpCompilation.Create(
            "SampleModule", [tree], References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, "断片がコンパイルできない:\n" + string.Join("\n", errors.Select(e => e.ToString())));

        // 本物の build は、ページが無くても印を空の値で渡す（build_property.SpoitableHasPanelPage = の行が出る）。同じ形にする。
        var options = new AnalyzerOptions([], new BuildProperties(hasPanelPage ? "true" : ""));
        var diagnostics = await compilation.WithAnalyzers([analyzer], options).GetAnalyzerDiagnosticsAsync();

        return [.. diagnostics.OrderBy(d => d.Location.SourceSpan.Start)];
    }

    /// <summary>診断 1 つを「番号 行: 文」の形に（行は 1 始まり。場所が無ければ 0）。</summary>
    public static string Describe(Diagnostic diagnostic) =>
        $"{diagnostic.Id} {(diagnostic.Location.IsInSource ? diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1 : 0)}: {diagnostic.GetMessage()}";

    private sealed class BuildProperties(string? hasPanelPage) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(hasPanelPage);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options(null);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options(null);

        private sealed class Options(string? hasPanelPage) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                value = key == SpoitableToolKit.Sdk.Analyzers.PanelRegistrationAnalyzer.HasPanelPageOption ? hasPanelPage : null;
                return value is not null;
            }
        }
    }
}
