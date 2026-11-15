namespace SpoitableToolKit.Abstractions.Ui;

/// <summary>
/// 自分の画面まわりを本体に渡す口（<see cref="IModuleHost.Ui"/>）。モジュール 1 つに 1 つあり、宛先は自分に固定されている。
/// <para>
/// <b>本体が出すのは、ここで登録したものだけ。</b> モジュールのクラスが <see cref="IUiEndpoint"/> や
/// <see cref="IContextMenuSource"/> を実装しているだけでは、サイドバーにも出ないし呼ばれもしない。
/// 登録するのは自分自身でも、別のクラスでもよい。
/// </para>
/// <code>
/// public bool OnInit(IModuleHost host)
/// {
///     host.Ui.RegisterPanel("index.html");   // サイドバーに出すページ（web/ からの相対パス）
///     host.Ui.RegisterEndpoint(this);         // そのページから呼ばれる口
///     host.Ui.RegisterStatus(_status);        // サイドバーの行の右・手首のタイル・ダッシュボードの札に出す様子
///     return true;
/// }
///
/// // あとはいつでも（どのスレッドからでも）
/// host.Ui.Push("status", JsonSerializer.Serialize(state, Json));
/// </code>
/// <para>
/// <b>Register で始まるものは <see cref="ISpoitableModule.OnInit"/> の中で、それぞれ 1 回だけ呼べる。</b>
/// 本体は OnInit が返った直後に、何を出すか（サイドバーの項目・空間の窓のページ・呼ばれる口・右クリックの項目・様子）を決める。
/// 外れたら <see cref="InvalidOperationException"/>: OnInit の中ならそのモジュールは読み込まれず、
/// あと（<see cref="ISpoitableModule.OnPostInit"/> など）ならそこで止まる。どちらも、何が足りないかを文で返す。
/// </para>
/// <para>
/// <see cref="Push"/> と <see cref="Show"/> は縛らない（OnInit の中でも後でも、どのスレッドからでもよい）。
/// </para>
/// </summary>
public interface IModuleUi
{
    /// <summary>
    /// サイドバーに出すページ（パネル）。<b><c>modules/&lt;Id&gt;/web/</c> からの相対パスで、<c>.html</c> で終わる</b>もの（例 <c>index.html</c>）。
    /// 使える字は英数字・<c>-</c>・<c>_</c> と、区切りの <c>/</c>。外れていたら <see cref="ArgumentException"/>。
    /// <para>
    /// ページは <c>spoitable://&lt;Id&gt;/</c> という<b>モジュール専用の origin</b> で配られ、シェルは iframe を 1 枚立てるだけ。中の画面設計はモジュールに任せる。
    /// 本体を呼ぶには <c>&lt;script src="/_host/bridge.js"&gt;</c> を読み込む（呼ぶ先は <see cref="RegisterEndpoint"/> で渡したもの）。
    /// </para>
    /// <para>
    /// <b>本体の部品の見た目は <c>&lt;link rel="stylesheet" href="/_host/ui.css"&gt;</c></b>（本体の画面と同じ 1 枚）。当たるのは <c>spoitable-</c> で始まる class だけで、
    /// ページの素の要素（<c>button</c> など）や自分の class には何も当てない。読む順は ui.css → 自分の CSS → <c>/_host/theme.css</c>（色・影・フォントのトークン。いちばん後）。
    /// 名前は <c>spoitable-&lt;部品&gt;</c>、中の部分は <c>spoitable-&lt;部品&gt;-&lt;部分&gt;</c>、形の違い・状態は <c>spoitable-&lt;部品&gt;-&lt;修飾&gt;</c> を部品の class と並べて付ける
    /// （例 <c>class="spoitable-btn spoitable-btn-primary"</c>）。使える部品:
    /// </para>
    /// <list type="bullet">
    /// <item><c>spoitable-btn</c> 押しボタン。修飾は <c>spoitable-btn-primary</c>（決めるボタン）・<c>-ghost</c>（枠の無い控えめなもの）・<c>-small</c>・<c>-icon</c>（アイコンだけの正方形）。
    /// <c>spoitable-ib</c> 枠の無い小さな四角いボタン（↑↓✕）。<c>spoitable-link</c> 字だけのボタン。</item>
    /// <item><c>&lt;label class="spoitable-search"&gt;</c> の中にアイコンと <c>&lt;input class="spoitable-search-input"&gt;</c> 検索の欄。</item>
    /// <item><c>&lt;input type="checkbox" class="spoitable-switch"&gt;</c> 入/切のスイッチ。</item>
    /// <item><c>spoitable-tabs</c> の中に <c>&lt;button class="spoitable-tab"&gt;</c>、開いているものに <c>spoitable-tab-on</c>。下線のタブ。</item>
    /// <item><c>spoitable-toolbar</c> 横に並べて折り返す帯、<c>spoitable-spacer</c> 残りの幅を取る詰め物。<c>spoitable-hint</c> 薄い小さな字。<c>spoitable-empty</c> 「何もない」の表示。</item>
    /// <item><c>spoitable-card</c> 枠で囲んだ面（見出しは中の <c>h2</c>。右にボタンを置くなら <c>spoitable-card-head</c> で包む）。
    /// <c>spoitable-tiles</c> の中に <c>spoitable-tile</c>（中は <c>spoitable-tile-label</c>・<c>spoitable-tile-value</c>・<c>spoitable-tile-note</c>）数字の札。</item>
    /// <item><c>spoitable-modal</c>（画面を覆う幕）の中に <c>spoitable-dialog</c>。中は <c>spoitable-dialog-head</c>（<c>spoitable-dialog-title</c> に <c>h2</c> と <c>p</c>）→ <c>spoitable-dialog-body</c> → <c>spoitable-dialog-foot</c>。</item>
    /// <item><c>class="spoitable-state" data-state="ok|off|warn|bad"</c> 状態の色（字の色。<see cref="ModuleStatus"/> の重さと同じ 4 つ）。</item>
    /// <item>選ぶ欄: <c>&lt;script src="/_host/pick.js"&gt;</c>・<c>/_host/when.js</c> を<b>この順に</b>読んで（暦の時・分が pick.js を使う）<c>spoitablePick.upgradeAll()</c>・<c>spoitableWhen.upgradeAll()</c> を呼ぶと、
    /// <c>&lt;select&gt;</c> と日時の欄に VR でも押せる部品（<c>spoitable-pick-value</c>・<c>spoitable-when-value</c>）が被さる（ネイティブのドロップダウンと暦は VR に出ない）。</item>
    /// </list>
    /// <para>
    /// ui.css は素の要素を整えない。<c>body</c> の地と字（<c>var(--bg)</c>・<c>var(--fg)</c>・<c>var(--font-ui)</c>）はページの CSS に書く。
    /// <c>hidden</c> 属性で部品を隠すなら <c>[hidden] { display: none !important; }</c> も置く（部品の <c>display</c> はブラウザの既定の <c>hidden</c> より強い）。
    /// </para>
    /// <para>
    /// <b>ファイルが <c>web/</c> に無ければ、本体は警告を出してサイドバーに出さない</b>（項目が出るのに中身が 404、にしない）。
    /// SDK の build は project の <c>web/</c> をそのまま出力へ写すので、ページは project の <c>web/</c> に置く。
    /// </para>
    /// </summary>
    /// <param name="entry">ページ。既定は無い（<c>index.html</c> でも書く）。</param>
    void RegisterPanel(string entry);

    /// <summary>
    /// <b>VR の空間の窓</b>で、パネルの代わりに出すページ（任意）。決まりは <see cref="RegisterPanel"/> と同じ。
    /// 登録しなければ、空間の窓でもパネルをそのまま出す。
    /// <para>
    /// 空間の窓は顔の近くに浮いていて、手のレーザーで操作する。大きな文字・大きなボタンのページにしたいときに使う。
    /// SteamVR のダッシュボードとデスクトップの窓はパネルのまま。
    /// <b>origin は同じ</b>（<c>spoitable://&lt;Id&gt;/</c>）なので、<see cref="RegisterEndpoint"/> の口へはどちらのページからも同じように届く。
    /// </para>
    /// <para>
    /// <b>先に <see cref="RegisterPanel"/> を呼ぶ</b>（パネルの代わりなので、パネルが無いと開く口が無い）。順が逆なら <see cref="InvalidOperationException"/>。
    /// ファイルが <c>web/</c> に無ければ、本体は警告を出して空間の窓でもパネルを出す。
    /// </para>
    /// </summary>
    void RegisterVrPanel(string entry);

    /// <summary>
    /// 自分のページ（パネル・VR のページ・手首のタイルのページ）から呼ばれる口。ページの <c>spoitable.call(method, payload)</c> が
    /// <see cref="IUiEndpoint.HandleUiCallAsync"/> に届く。登録しなければ、ページからの呼び出しは「口が無い」の失敗で返る。
    /// </summary>
    void RegisterEndpoint(IUiEndpoint endpoint);

    /// <summary>
    /// 右クリック（VR では長押し）のメニューに項目を足す口。メニューは自分のページの上で開いたときに聞かれる（<see cref="IContextMenuSource"/>）。
    /// </summary>
    void RegisterContextMenu(IContextMenuSource source);

    /// <summary>
    /// いまの様子を短い文字で出す口（<see cref="ModuleStatus"/>）。サイドバーの行の右・手首のタイル・ダッシュボードの札に出る。
    /// <para>
    /// <b>インスタンスを本体と共有する</b>: 登録したあとは、手元のこれを書き換えるだけで画面に届く（呼び直さない）。
    /// <b>押すとこのモジュールのパネルが開く</b>。
    /// </para>
    /// </summary>
    void RegisterStatus(ModuleStatus status);

    /// <summary>
    /// 自分のページへ 1 件流す（<see cref="IUiEndpoint"/> の逆向き）。どのスレッドから呼んでもよい。
    /// <para>
    /// <b>これは best-effort であって配送保証ではない。</b> ページが開いていなければ捨てられる
    /// （iframe はサイドバーで初めて選ばれたときに作られる）。<b>正となる状態は C# 側に持ち、ページは開いたときに自分で取り直す</b>:
    /// </para>
    /// <code>
    /// // モジュール側
    /// _failures.Add(...);
    /// host.Ui.Push("failures", JsonSerializer.Serialize(_failures, Json));   // 開いていれば届く
    ///
    /// // ページ側（bridge.js）
    /// const state = await spoitable.call('getFailures');   // 読み込み時に現在値を取る
    /// spoitable.onPush((channel, payload) =&gt; { ... });     // 以降の変化を受ける
    /// </code>
    /// <para>
    /// 差分ではなく<b>そのチャンネルの現在値をまるごと</b>送るほうが扱いやすい（取りこぼしても次の push で追いつける）。
    /// 「ページが開いているか」を問い合わせる口は<b>わざと無い</b> — 面が 2 つ（デスクトップの窓 / VR）ある以上、
    /// 開いているときだけ状態を進めるモジュールは、どちらの見え方も信用できなくなる。
    /// </para>
    /// </summary>
    /// <param name="channel">モジュールの中での区別。ページの <c>onPush</c> に第 1 引数として渡る。</param>
    /// <param name="payloadJson">
    /// JSON 文字列。<see cref="IUiEndpoint.HandleUiCallAsync"/> の戻り値と同じ形にしてあるので、モジュール自身の <c>JsonSerializerOptions</c> の命名がそのまま通る。
    /// JSON として読めないものは、本体が警告を出して捨てる。
    /// </param>
    void Push(string channel, string payloadJson);

    /// <summary>
    /// 自分のパネルを開かせる。フッターの項目を押されたときのような、<b>利用者の操作を受けて画面を切り替えたい</b>ときに使う。
    /// <para>
    /// <b>サイドバーの無い面では何もしない</b>（VR の窓や、画面なしの起動）。<b>例外にはしない</b> —
    /// 画面があるかどうかでモジュールのコードが分かれると、面が増えるたびに全モジュールが直る必要が出る。
    /// パネルを登録していないモジュールが呼んでも何も起きない。
    /// </para>
    /// </summary>
    void Show();

    /// <summary>
    /// 自分のパネルを <b>VR の空間の窓</b>で開く。<b>空間の窓がもう自分のパネルを出していれば閉じる</b>（別の画面を出していれば切り替える）。
    /// 手首のショートカットの <see cref="Shortcuts.IClickableWristShortcut.OnTriggerAsync"/> から呼ぶ形を想定している。どのスレッドから呼んでもよい。
    /// <para>
    /// <paramref name="place"/> を渡すと、パネルは URL の <c>#</c> の後ろにそれを受け取る（<c>location.hash</c> / <c>hashchange</c>）。
    /// 1 つのパネルの中の別の場所を、別々のショートカットにできる。<b>英数字・<c>-</c>・<c>_</c> だけ</b>（64 字まで。外れていたら <see cref="ArgumentException"/>）。
    /// </para>
    /// <para>
    /// <b>パネルが無い（登録していない・ファイルが無かった）・VR が動いていないときは何もしない</b>（例外にはしない。<see cref="Show"/> と同じ理由）。
    /// </para>
    /// </summary>
    void ToggleVrPanel(string? place = null);
}
