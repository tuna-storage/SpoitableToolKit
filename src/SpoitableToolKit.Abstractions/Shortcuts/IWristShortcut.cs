using SpoitableToolKit.Abstractions.Ui;

namespace SpoitableToolKit.Abstractions.Shortcuts;

/// <summary>
/// 手首のショートカット（タイル 1 枚）の、<b>どの種類にも共通の部分</b>（出す情報）。これだけを実装しても登録できない。
/// <b>押すだけのタイルは <see cref="IClickableWristShortcut"/>、ドラッグだけのタイルは <see cref="IDraggableWristShortcut"/></b> を実装し、
/// <see cref="IModuleShortcuts.Register(IClickableWristShortcut)"/> / <see cref="IModuleShortcuts.Register(IDraggableWristShortcut)"/> で渡す
/// （<see cref="ISpoitableModule.OnInit"/> の中で。いくつでもよい）。<b>両方は実装できない</b>（登録のときに例外）。
/// <para>
/// 利用者は設定の「手首のショートカット」で、登録されたものから好きなだけ選んで並べる。設定の値は <c>&lt;モジュールの Id&gt;/&lt;<see cref="Id"/>&gt;</c>。
/// <b>パネルを持つモジュールには、登録しなくても 1 つある</b>（そのパネルを開くもの。設定の値はモジュールの Id）。ここで登録するのはそれ以外の分。
/// </para>
/// <code>
/// public bool OnInit(IModuleHost host)
/// {
///     host.Shortcuts.Register(new ResolveLatestShortcut(this));
///     return true;
/// }
///
/// sealed class ResolveLatestShortcut(MyModule module) : IClickableWristShortcut
/// {
///     public string Id => "resolve-latest";
///     public string Name => "mymodule.shortcut.resolveLatest";   // 辞書のキー
///     public Task OnTriggerAsync(CancellationToken cancellationToken) => module.ResolveLatestAsync(cancellationToken);
/// }
/// </code>
/// <para>
/// <b><see cref="Id"/>・<see cref="Name"/>・<see cref="IconPath"/>・<see cref="Page"/>・<see cref="Status"/>・<see cref="MarkedPanel"/> は登録したときに 1 回だけ読む</b>
/// （あとから値を変えても並びには出ない）。<see cref="Status"/> の中身だけは、書き換えればそのたびにタイルへ届く。
/// </para>
/// </summary>
public interface IWristShortcut
{
    /// <summary>
    /// モジュールの中で一意な名前。<b>英小文字・数字・<c>-</c> だけ</b>（頭は英小文字か数字。設定に <c>&lt;モジュール&gt;/&lt;Id&gt;</c> の形で残る）。
    /// 外れた字・同じ Id の 2 回目は、登録のときに例外。<b>一度出したら変えない</b> — 変えると、利用者が並べたタイルが黙って消える。
    /// </summary>
    string Id { get; }

    /// <summary>
    /// タイルと設定に出す名前。<b>辞書のキー</b>（<c>locales/&lt;言語&gt;.json</c> に置いたもの。例 <c>videourl.shortcut.resolveLatest</c>）を渡すと、
    /// 画面の言語で引かれる。辞書に無ければ字のまま出す。空なら登録のときに例外。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 線のアイコン（<see cref="ISpoitableModule.IconPath"/> と同じ決まり。SVG の <c>path</c> の <c>d</c> だけ）。
    /// <c>null</c> か、パスの字以外が混ざっていたら、モジュールのアイコン。
    /// </summary>
    string? IconPath => null;

    /// <summary>
    /// タイルの中に描くページ（任意）。<c>modules/&lt;モジュールの Id&gt;/web/</c> からの相対パスで、<b><c>.html</c> で終わる</b>もの（例 <c>tile.html</c>）。
    /// 使える字は英数字・<c>-</c>・<c>_</c> と、区切りの <c>/</c>（外れていたら登録のときに例外）。<c>null</c> なら、タイルはアイコン・名前・<see cref="Status"/> の文字で描かれる。
    /// <para>
    /// ページはタイル 1 マス（幅 112 px、高さ 90〜102 px）の iframe に、パネルと同じ origin（<c>spoitable://&lt;Id&gt;/</c>）で開く。
    /// <c>&lt;script src="/_host/bridge.js"&gt;</c> を読めば、パネルと同じく <c>spoitable.call</c>（<see cref="IModuleUi.RegisterEndpoint"/> で登録した口）と
    /// <c>spoitable.onPush</c>（<see cref="IModuleUi.Push"/>）が使える。テーマは <c>/_host/theme.css</c>、本体の部品は <c>/_host/ui.css</c>（一覧は <see cref="IModuleUi.RegisterPanel"/>）。背景を描かなければタイルの地が見える。
    /// </para>
    /// <para>
    /// iframe を作るのは、手首の<b>いま出ているページのタイルだけ</b>（6 つまで）。ページを送ると、前のタイルのページは閉じる。
    /// 正となる状態は C# に持ち、ページは開いたときに <c>spoitable.call</c> で取り直すこと（パネルと同じ）。
    /// ファイルが <c>web/</c> に無ければ、本体が警告を出して<b>ページ無しのタイル</b>として並べる。
    /// </para>
    /// <para>
    /// <b>ページは描くだけ。押す・ドラッグはページに届かない</b> — タイルが受けて、本体が <see cref="IClickableWristShortcut.OnTriggerAsync"/>・
    /// <see cref="IDraggableWristShortcut.OnStartDragAsync"/> などを呼ぶ。押されて見た目を変えるなら、そこから <see cref="IModuleUi.Push"/> で描き直させる。
    /// </para>
    /// </summary>
    string? Page => null;

    /// <summary>
    /// タイルに出す中身（<see cref="ModuleStatus.Text"/> と <see cref="ModuleStatus.State"/> の色）。<c>null</c> か空なら出さない。
    /// <see cref="IModuleUi.RegisterStatus"/> と同じで<b>インスタンスを本体と共有する</b> — 書き換えるだけで届く。どのスレッドから書いてもよい。
    /// <see cref="ModuleStatus.Note"/> はタイルには出ない。
    /// </summary>
    ModuleStatus? Status => null;

    /// <summary>
    /// <b>空間の窓がこの画面を出している間、タイルに枠を付ける</b>（任意）。押すと自分のパネルを開くタイルは、開く画面をここで名乗る。
    /// <para>
    /// <b>名乗るだけで、開くのは自分の処理の中</b>（<see cref="IModuleUi.ToggleVrPanel"/>）。押してから開くまでに何をしてもよい。
    /// 本体は押す前に開く先を知らないので、名乗らなければ枠は付かない。<c>ToggleVrPanel</c> に渡す場所と同じものを書く
    /// （<c>ToggleVrPanel()</c> なら <c>new PanelView()</c>、<c>ToggleVrPanel("stats")</c> なら <c>new PanelView { Place = "stats" }</c>）。
    /// 押すだけのタイル（<see cref="IClickableWristShortcut"/>）は、押した瞬間に枠を先に塗る（開いていれば消す）。
    /// </para>
    /// <para>登録したときに 1 回だけ読む。場所の字が外れていたら登録のときに例外。</para>
    /// </summary>
    PanelView? MarkedPanel => null;
}
