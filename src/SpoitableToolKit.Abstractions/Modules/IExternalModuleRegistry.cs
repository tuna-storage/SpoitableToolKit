namespace SpoitableToolKit.Abstractions.Modules;

/// <summary>
/// モジュール間でサービス（API）をやり取りする場所。<b>名前で登録し、名前で取る。</b>
/// <para>
/// 登録は <see cref="ISpoitableModule.OnPostInit"/> で、取得は
/// <see cref="ISpoitableModule.OnAllModulesLoaded"/> 以降で行う。この順序はホストが保証する。
/// </para>
/// <para>
/// <b>名前は、登録するモジュールの Id で始める</b>（<c>&lt;Id&gt;.&lt;何の API か&gt;</c>。例: Id が <c>hrs</c> なら <c>hrs.api</c>）。
/// Id のあとは a-z・0-9・<c>_</c> <c>-</c> だけ（<c>.</c> は入れない。名前の持ち主が「最後の <c>.</c> の手前」で 1 つに決まるように）。
/// 同じ名前は 1 回しか登録できない。<b>型引数には契約の interface を書く</b>（書き忘れると実体の型になるので、本体が断る）。
/// 名前は<b>契約の側に定数で置く</b>と、渡す側と使う側で食い違わない:
/// <code>
/// public interface IHeartRateApi
/// {
///     /// &lt;summary&gt;この API の名前。渡す側は登録に、使う側は取得に、これをそのまま渡す。&lt;/summary&gt;
///     const string ModuleIdentity = "hrs.api";
///
///     int? CurrentBpm { get; }
/// }
///
/// // 渡す側（Id = "hrs"）の OnPostInit:
/// host.ExternalModules.Register&lt;IHeartRateApi&gt;(this, IHeartRateApi.ModuleIdentity, new HeartRateApi(...));
///
/// // 使う側の OnAllModulesLoaded:
/// var api = host.ExternalModules.GetRequired&lt;IHeartRateApi&gt;(IHeartRateApi.ModuleIdentity);
/// </code>
/// </para>
/// <para>
/// <b>契約の型は、渡す側のモジュールの <c>shared/</c> に置く dll に入れること</b>（<c>modules/&lt;Id&gt;/shared/&lt;Id&gt;.Api.dll</c> など）。
/// 使う側はその dll を参照するだけで、自分のフォルダには入れない。モジュール本体の dll に置いたり、両方が写しを持ったりすると、
/// 同じ名前の<b>別の型</b>になって取得が失敗する（失敗の文に、どちらの dll から来た型かが出る）。
/// 壊す変更をするときは、型か名前（<c>hrs.api</c> → <c>hrs.api2</c>）を変える。
/// </para>
/// </summary>
public interface IExternalModuleRegistry
{
    /// <summary>
    /// 自分が提供するサービスを公開する。<see cref="ISpoitableModule.OnPostInit"/> で呼ぶ。
    /// </summary>
    /// <param name="owner">登録するモジュール自身（<c>this</c>）。落ちたときに、ホストがその登録をまとめて外す。</param>
    /// <typeparam name="T">契約の interface（渡す側の <c>shared/</c> の dll に置いたもの）。</typeparam>
    /// <param name="name"><c>&lt;owner の Id&gt;.&lt;何の API か&gt;</c>（例 <c>hrs.api</c>）。</param>
    /// <param name="implementation">実体。</param>
    /// <exception cref="ArgumentException">
    /// 名前が <paramref name="owner"/> の Id で始まっていない・使えない字がある、または <typeparamref name="T"/> が interface でない。
    /// </exception>
    /// <exception cref="InvalidOperationException">その名前はもう登録されている。</exception>
    void Register<T>(ISpoitableModule owner, string name, T implementation) where T : class;

    /// <summary>
    /// 必須の依存として取る。<see cref="ISpoitableModule.OnAllModulesLoaded"/> 以降で呼ぶ。
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// その名前の登録が無い（文に、同じ型で登録されている名前を並べる）、または登録はあるが型が違う（文に、どちらの dll から来た型かを出す）。
    /// </exception>
    IExternalModuleRef<T> GetRequired<T>(string name) where T : class;

    /// <summary>任意の依存として取る。その名前の登録が無ければ null。</summary>
    /// <exception cref="InvalidOperationException">登録はあるが型が違う（黙って null にしない。契約の dll が二重になっている印なので）。</exception>
    IExternalModuleRef<T>? GetOptional<T>(string name) where T : class;

    /// <summary>
    /// <typeparamref name="T"/> として使える登録を全部（名前 → 参照）。同じ契約を何人も渡すとき（例: いくつもの入力元）に使う。
    /// 1 つも無ければ空。<b>並びに意味は無い</b>（順番が要るなら、名前で並べるか、契約の側に順位を持たせる）。
    /// </summary>
    /// <exception cref="InvalidOperationException">同じ名前の別の型で登録されているものがある（契約の dll が二重になっている）。</exception>
    IReadOnlyDictionary<string, IExternalModuleRef<T>> GetAll<T>() where T : class;
}
