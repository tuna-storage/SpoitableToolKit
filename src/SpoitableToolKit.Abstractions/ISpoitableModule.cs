using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Modules;
using SpoitableToolKit.Abstractions.Ui;

namespace SpoitableToolKit.Abstractions;

/// <summary>
/// ホストが読み込むモジュールのエントリポイント。
/// <para>
/// モジュールは「常駐バックグラウンドサービス」＋「任意の表示パネル」の 2 部構成で、
/// UI 無しでも成立すること（CEF は遅延起動なので、UI は表示層でしかない）。
/// パネル・ページから呼ばれる口・様子は、<see cref="OnInit"/> の中で <see cref="IModuleHost.Ui"/> に登録する（このクラスに interface を実装するだけでは出ない）。
/// </para>
/// <para>
/// 読み込みは 3 段階に分かれる。**他モジュールのサービスを取ってよいのは
/// <see cref="OnAllModulesLoaded"/> 以降**で、それより前は登録が揃っていない。
/// </para>
/// <code>
/// 全モジュールの OnInit           … 自分の初期化。他には触れない
/// 全モジュールの OnPostInit       … 自分が提供するものを登録する
/// 全モジュールの OnAllModulesLoaded … ここで初めて他を取ってよい
/// </code>
/// </summary>
public interface ISpoitableModule
{
    /// <summary>
    /// 安定した識別子。<b>モジュールのクラスには書かない</b> — csproj の <c>&lt;SpoitableModuleId&gt;</c> を、SDK の build が dll に刻み
    /// （<c>ModuleInfo.Id</c> と同じ値）、これがそれを返す。
    /// <para>
    /// 実行時のフォルダ名（<c>modules/&lt;Id&gt;/</c>）と同じでなければ、<b>本体は読み込まない</b>（フォルダの名前を手で変えた写しを、別のモジュールとして動かさない）。
    /// 設定・記録の置き場、翻訳キーの頭（<c>&lt;Id&gt;.…</c>）、<c>spoitable://&lt;Id&gt;/</c>、OFF にする相手の名前に使う。
    /// 変えると、利用者の設定と記録は読まれなくなる。
    /// </para>
    /// <para>
    /// SDK を通さずに作った dll（刻まれていないもの）で読むと <see cref="InvalidOperationException"/>。
    /// </para>
    /// </summary>
    string Id => Stamped(GetType(), ModuleIdKey, nameof(Id), "SpoitableModuleId");

    /// <summary>
    /// UI に表示する名前。<b>モジュールのクラスには書かない</b> — csproj の <c>&lt;SpoitableModuleName&gt;</c> を、SDK の build が dll に刻み
    /// （<c>ModuleInfo.Name</c> と同じ値）、これがそれを返す。入れるときに見せる名前（<c>module.json</c>）と同じになる。
    /// </summary>
    string DisplayName => Stamped(GetType(), ModuleNameKey, nameof(DisplayName), "SpoitableModuleName");

    /// <summary>SDK の build が Id を刻む <see cref="System.Reflection.AssemblyMetadataAttribute"/> のキー（SDK の targets と同じ字）。</summary>
    internal const string ModuleIdKey = "SpoitableToolKit.ModuleId";

    /// <summary>SDK の build が名前を刻む <see cref="System.Reflection.AssemblyMetadataAttribute"/> のキー。</summary>
    internal const string ModuleNameKey = "SpoitableToolKit.ModuleName";

    /// <summary>dll ごとに 1 回だけ読む（Id は本体が何度も読む — UI からの呼び出しのたび、ページの配信のたび）。</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Reflection.Assembly, IReadOnlyDictionary<string, string>> Stamps = new();

    private static string Stamped(Type type, string key, string member, string property)
    {
        var stamps = Stamps.GetValue(type.Assembly, static assembly =>
            System.Reflection.CustomAttributeExtensions.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(assembly)
                .Where(a => a.Value is { Length: > 0 })
                .GroupBy(a => a.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Value!, StringComparer.Ordinal));

        if (stamps.TryGetValue(key, out var value)) return value;

        throw new InvalidOperationException(
            $"{type.FullName} has no {member}: its dll ({type.Assembly.GetName().Name}) was not built with the SpoitableToolKit.Sdk package, " +
            $"which writes <{property}> from the csproj into the dll. Reference the SDK (<PackageReference Include=\"SpoitableToolKit.Sdk\" ... />) " +
            $"and set <{property}> in the csproj; do not write {member} in the module class.");
    }

    string? Author => null;

    /// <summary>
    /// サイドバーに出すアイコン。絵文字 1 文字程度を想定する。
    /// 未指定なら <see cref="DisplayName"/> の頭文字が使われる。<see cref="IconPath"/> があればそちらが勝つ。
    /// </summary>
    string? Icon => null;

    /// <summary>
    /// サイドバー・手首のショートカットに出す線のアイコン。<b>SVG の <c>path</c> の <c>d</c> だけ</b>
    /// （24×24 の枠、線の太さ 1.8、塗りなし、色は字の色）。<c>null</c> なら <see cref="Icon"/>。
    /// <para>
    /// <b>HTML は渡せない。</b> パスの字（数字・空白・<c>MmLlHhVvCcSsQqTtAaZz</c>・<c>.,-</c>）以外が混ざっていたら、
    /// ホストは使わずに <see cref="Icon"/> へ落とす。
    /// </para>
    /// </summary>
    string? IconPath => null;

    /// <summary>
    /// 設定。<c>null</c> なら設定画面に出さない。
    /// <para>
    /// <b>インスタンスはホストと共有する。</b> 画面で変えた値はここへ直接書かれるので、
    /// モジュールは使うときに読めばよい。<see cref="OnInit"/> より前に保存済みの値が入る。
    /// </para>
    /// </summary>
    IModuleConfig? Config => null;

    /// <summary>
    /// 自分の初期化。<c>false</c> を返すとこのモジュールの読み込みを中止する
    /// （必要な環境が無い、設定が壊れている等）。
    /// <para>
    /// <b>画面まわり（<see cref="IModuleHost.Ui"/> の Register…）と手首のショートカット（<see cref="IModuleHost.Shortcuts"/>）の登録はここでだけできる。</b>
    /// 返った直後に、本体が何を出すかを決める。
    /// </para>
    /// <para>
    /// この時点で CEF は未初期化なので UI に触れないこと。
    /// 他モジュールもまだ何も登録していないので取りに行かないこと。
    /// </para>
    /// </summary>
    bool OnInit(IModuleHost host);

    /// <summary>
    /// 自分が提供するサービスを <see cref="IExternalModuleRegistry.Register{T}"/> で公開する。
    /// 取りに行くのはまだ早い（他モジュールの <see cref="OnPostInit"/> が済んでいない）。
    /// </summary>
    void OnPostInit() { }

    /// <summary>
    /// 全モジュールの <see cref="OnPostInit"/> が終わったあとに呼ばれる。
    /// 他モジュールのサービスを取ってよいのはここから。
    /// </summary>
    void OnAllModulesLoaded() { }

    /// <summary>後片付け。登録したサービスはホストが自動で外すので、自分で外す必要はない。</summary>
    void OnShutdown() { }
}
