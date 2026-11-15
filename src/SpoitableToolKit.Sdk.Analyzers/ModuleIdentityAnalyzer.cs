using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SpoitableToolKit.Sdk.Analyzers;

/// <summary>
/// STK040: モジュールのクラスが <c>ISpoitableModule.Id</c> か <c>ISpoitableModule.DisplayName</c> を<b>手で書いた</b>。
/// この 2 つは SDK の build が csproj（<c>SpoitableModuleId</c>・<c>SpoitableModuleName</c>）から dll に刻み、<c>ISpoitableModule</c> の既定の本体がそれを返す。
/// 手で書くと csproj の値とずれる（ずれると本体は読み込まない）ので、build で止める。
/// <para>
/// 当たるのは: クラスの暗黙の実装（<c>public string Id =&gt; …</c>。基底のクラスが書いたものも）・明示の実装（<c>string ISpoitableModule.Id =&gt; …</c>）・
/// <c>ISpoitableModule</c> を継いだ interface が既定の本体を書き直したもの。<b>書いた所ごとに 1 回</b>（派生のクラスの数だけ重ねない）。
/// このコンパイルのソースに無いもの（別の dll が書いたもの）は見ない（その dll の build が見る）。
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ModuleIdentityAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "STK040";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "The module's Id or DisplayName is written by hand",
        messageFormat: "{0} writes the module's {1} by hand. Remove it: the SDK writes SpoitableModuleId and SpoitableModuleName from the csproj into the dll, " +
                       "and ISpoitableModule.Id and ISpoitableModule.DisplayName already return them. " +
                       "To change the Id or the name, change <SpoitableModuleId> or <SpoitableModuleName> in the csproj.",
        category: "SpoitableToolKit",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// 同じ STK040 の、<b>そのプロパティがほかの interface の member も兼ねているとき</b>の文（例: モジュールのクラス自身を手首のショートカットにして、
    /// <c>public string Id</c> がショートカットの Id も兼ねる）。「消す」とほかの interface が壊れ、書き戻すと STK040 に戻るので、直し方を変える:
    /// ほかの interface の側を明示の実装にする。
    /// </summary>
    public static readonly DiagnosticDescriptor SharedRule = new(
        DiagnosticId,
        title: "The module's Id or DisplayName is written by hand",
        messageFormat: "{0}.{1} is also the module's {1}, which the SDK writes from the csproj into the dll (ISpoitableModule.{1} already returns it). " +
                       "Keep it for {2} only by writing it explicitly (string {2}.{1} => ...) instead of a public {1}, so that ISpoitableModule.{1} is left to the SDK. " +
                       "To change the module's Id or name, change <SpoitableModuleId> or <SpoitableModuleName> in the csproj.",
        category: "SpoitableToolKit",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule, SharedRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName(KnownTypes.SpoitableModule) is not { } module) return;

            var stamped = module.GetMembers().OfType<IPropertySymbol>().Where(p => p.Name is "Id" or "DisplayName").ToImmutableArray();
            if (stamped.IsEmpty) return;

            // 書いた所ごとに 1 回だけ出す（基底のクラスを継いだクラスがいくつあっても）。**コンパイルごとに持つ** — アナライザーの
            // インスタンスはコンパイラのサーバーが使い回すので、フィールドに置くと次の build に前の分が残る。
            var reported = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);

            start.RegisterSymbolAction(symbol => CheckType((INamedTypeSymbol)symbol.Symbol, module, stamped, reported, symbol), SymbolKind.NamedType);
            start.RegisterSymbolAction(symbol => CheckExplicit((IPropertySymbol)symbol.Symbol, stamped, reported, symbol), SymbolKind.Property);
        });
    }

    /// <summary>モジュールのクラス（構造体も）で、Id・DisplayName の実装がこのコンパイルのソースにあれば出す（基底のクラスが書いたものも）。</summary>
    private static void CheckType(
        INamedTypeSymbol type, INamedTypeSymbol module, ImmutableArray<IPropertySymbol> stamped, ConcurrentDictionary<ISymbol, bool> reported,
        SymbolAnalysisContext context)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct)) return;
        if (!type.AllInterfaces.Contains(module, SymbolEqualityComparer.Default)) return;

        foreach (var member in stamped)
        {
            // 既定の本体のままなら、ISpoitableModule 自身の member（別の dll）が返る。
            if (type.FindImplementationForInterfaceMember(member) is IPropertySymbol written)
                Report(written, member, AlsoImplements(type, module, written, member.Name), reported, context);
        }
    }

    /// <summary>
    /// <paramref name="written"/> が、ISpoitableModule（とそれを継いだ interface）のほかに、同じ名前で実装している interface（例 <c>IWristShortcut</c>）。無ければ null。
    /// 名前の順で最初のもの（並べて読むので、毎回同じ文にする）。
    /// </summary>
    private static INamedTypeSymbol? AlsoImplements(INamedTypeSymbol type, INamedTypeSymbol module, IPropertySymbol written, string name) =>
        type.AllInterfaces
            .Where(other => !SymbolEqualityComparer.Default.Equals(other, module) && !other.AllInterfaces.Contains(module, SymbolEqualityComparer.Default))
            .Where(other => other.GetMembers(name).OfType<IPropertySymbol>()
                .Any(property => SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(property)?.OriginalDefinition, written.OriginalDefinition)))
            .OrderBy(other => other.Name, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>明示の実装（<c>string ISpoitableModule.Id =&gt; …</c>）。ISpoitableModule を継いだ interface が既定の本体を書き直したものも、ここで当たる。</summary>
    private static void CheckExplicit(
        IPropertySymbol property, ImmutableArray<IPropertySymbol> stamped, ConcurrentDictionary<ISymbol, bool> reported, SymbolAnalysisContext context)
    {
        foreach (var implemented in property.ExplicitInterfaceImplementations)
        {
            foreach (var member in stamped)
            {
                if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, member)) Report(property, member, null, reported, context);
            }
        }
    }

    /// <param name="shared">同じプロパティが兼ねているほかの interface（<see cref="SharedRule"/> の文にする）。</param>
    private static void Report(
        IPropertySymbol written, IPropertySymbol member, INamedTypeSymbol? shared, ConcurrentDictionary<ISymbol, bool> reported, SymbolAnalysisContext context)
    {
        // **元の定義で数える** — 総称型の基底（Base<T>）が書いたものは、継いだクラスごとに Base<int>.Id・Base<string>.Id という別の symbol で返るが、書いた行は 1 つ。
        var definition = written.OriginalDefinition;
        if (definition.Locations.FirstOrDefault(location => location.IsInSource) is not { } where) return;
        if (!reported.TryAdd(definition, true)) return;

        context.ReportDiagnostic(shared is null
            ? Diagnostic.Create(Rule, where, definition.ContainingType.Name, member.Name)
            : Diagnostic.Create(SharedRule, where, definition.ContainingType.Name, member.Name, shared.Name));
    }
}
