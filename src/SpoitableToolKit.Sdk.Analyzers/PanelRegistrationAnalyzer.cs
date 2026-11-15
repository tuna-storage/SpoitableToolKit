using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SpoitableToolKit.Sdk.Analyzers;

/// <summary>
/// STK041: モジュールに <c>web/index.html</c> があるのに、コードのどこにも <c>host.Ui.RegisterPanel</c> の呼び出しが無い（警告）。
/// 本体は登録したパネルだけをサイドバーに出すので、登録し忘れると「パネルを作ったのに出ない」だけが残る。
/// <para>
/// <c>web/index.html</c> の有無は SDK の targets が <c>SpoitableHasPanelPage</c> で渡す（アナライザーはファイルを探さない）。種類が Module の project だけ。
/// 呼んだかは、呼び出しがあるかだけを見る（<c>if</c> の中でも、メソッドとして渡しても、呼んだことにする）。どのページを渡したかは見ない。
/// 警告は <c>ISpoitableModule</c> を実装したクラスの所に出す（2 つ以上あれば名前の順で最初のもの、無ければ場所なし）。
/// </para>
/// <para>
/// 見えるのは<b>このコンパイルのソースの呼び出しだけ</b>。別の dll（自分の Library など）の拡張メソッドの中で呼んでいるなら、その呼び出しは見えずに警告が出る
/// （build は止めない）。そのときは project で <c>&lt;NoWarn&gt;$(NoWarn);STK041&lt;/NoWarn&gt;</c> にする。
/// ページが無いときも、build は印を空の値で渡す（<c>true</c> のときだけ見る）。
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PanelRegistrationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "STK041";

    /// <summary>SDK の targets が渡す、<c>web/index.html</c> があるかの印（<c>CompilerVisibleProperty</c>）。</summary>
    public const string HasPanelPageOption = "build_property.SpoitableHasPanelPage";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "web/index.html is there but the panel is never registered",
        messageFormat: "This module has web/index.html but never calls host.Ui.RegisterPanel, so its panel is not shown in the sidebar. " +
                       "Call host.Ui.RegisterPanel(\"index.html\") in OnInit, or remove web/index.html if the module has no panel.",
        category: "SpoitableToolKit",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            if (!start.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(HasPanelPageOption, out var hasPage) ||
                !string.Equals(hasPage?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                return;

            if (start.Compilation.GetTypeByMetadataName(KnownTypes.ModuleUi) is not { } ui ||
                start.Compilation.GetTypeByMetadataName(KnownTypes.SpoitableModule) is not { } module)
                return;

            var register = ui.GetMembers("RegisterPanel").OfType<IMethodSymbol>().ToImmutableArray();
            if (register.IsEmpty) return;

            // **コンパイルごとに持つ**（インスタンスはコンパイラのサーバーが使い回す）。呼び出しと型は並べて読まれるので、書くのは並んでよい形で。
            var seen = new Seen();

            start.RegisterOperationAction(operation =>
            {
                var method = operation.Operation switch
                {
                    IInvocationOperation invocation => invocation.TargetMethod,
                    IMethodReferenceOperation reference => reference.Method,
                    _ => null,
                };

                if (method is not null && IsRegisterPanel(method, ui, register)) seen.Called();
            }, OperationKind.Invocation, OperationKind.MethodReference);

            start.RegisterSymbolAction(symbol =>
            {
                var type = (INamedTypeSymbol)symbol.Symbol;
                if (type.TypeKind == TypeKind.Class && !type.IsAbstract && type.AllInterfaces.Contains(module, SymbolEqualityComparer.Default))
                    seen.Module(type);
            }, SymbolKind.NamedType);

            start.RegisterCompilationEndAction(end =>
            {
                if (seen.WasCalled) return;

                end.ReportDiagnostic(Diagnostic.Create(Rule, seen.FirstModuleLocation() ?? Location.None));
            });
        });
    }

    /// <summary><c>IModuleUi.RegisterPanel</c> そのものか、それを実装したメソッド（<c>IModuleUi</c> を実装したクラスで呼んだとき）。</summary>
    private static bool IsRegisterPanel(IMethodSymbol method, INamedTypeSymbol ui, ImmutableArray<IMethodSymbol> register)
    {
        var original = method.OriginalDefinition;
        if (register.Contains(original, SymbolEqualityComparer.Default)) return true;

        var owner = original.ContainingType;
        if (owner is null || !owner.AllInterfaces.Contains(ui, SymbolEqualityComparer.Default)) return false;

        return register.Any(member => SymbolEqualityComparer.Default.Equals(owner.FindImplementationForInterfaceMember(member), original));
    }

    /// <summary>1 回のコンパイルの中で見たもの。呼び出しと型の読みは別のスレッドから来る。</summary>
    private sealed class Seen
    {
        private readonly ConcurrentBag<INamedTypeSymbol> _modules = new();

        private int _called;

        public bool WasCalled => Volatile.Read(ref _called) == 1;

        public void Called() => Interlocked.Exchange(ref _called, 1);

        public void Module(INamedTypeSymbol type) => _modules.Add(type);

        /// <summary>警告を出す所。名前の順で最初のモジュールのクラス（並べて読むので、見た順は毎回違う）。</summary>
        public Location? FirstModuleLocation() =>
            _modules
                .OrderBy(type => type.ToDisplayString(), StringComparer.Ordinal)
                .Select(type => type.Locations.FirstOrDefault(location => location.IsInSource))
                .FirstOrDefault(location => location is not null);
    }
}
