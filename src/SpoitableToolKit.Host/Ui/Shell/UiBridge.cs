using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.App;
using SpoitableToolKit.Host.App.Startup;
using SpoitableToolKit.Host.AppLog;
using SpoitableToolKit.Host.Clipboard;
using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Host.Config.Models;
using SpoitableToolKit.Host.Localization;
using SpoitableToolKit.Host.Notifications;
using SpoitableToolKit.Host.Modules;
using SpoitableToolKit.Host.Modules.Management;
using SpoitableToolKit.Host.Modules.Management.Models;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Shortcuts;
using SpoitableToolKit.Host.Shortcuts.Gestures;
using SpoitableToolKit.Host.Status;
using SpoitableToolKit.Host.Ui.Browser;
using SpoitableToolKit.Host.Ui.ContextMenu;
using SpoitableToolKit.Host.Ui.Licenses;
using SpoitableToolKit.Host.Ui.LogView;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Host.Updates;
using SpoitableToolKit.Host.Updates.Models;
using SpoitableToolKit.Host.VrcLog.Store;

namespace SpoitableToolKit.Host.Ui.Shell;

/// <summary>
/// JS から呼べるホスト側の窓口。CefSharp が <c>stkHost</c> という名前で bind する。メソッド名は JS 側では camelCase になる（<c>GetStatus</c> → <c>stkHost.getStatus(token)</c>）。
/// 公開メソッドは全て第 1 引数に token（<see cref="UiToken"/>）を取る: bind はブラウザ単位なので、モジュールの iframe からも <c>BindObjectAsync('stkHost')</c> は通ってしまう。bind できても呼べない形にして塞いである（token はシェルのフレームにだけ配られ、別 origin のモジュールからは読めない）。
/// public なプロパティを生やさないこと — CefSharp は公開メンバを JS へ出すので、token やそれに準ずるものが乗ると、この仕組みごと無意味になる。
/// </summary>
internal sealed class UiBridge(
    IVrcLogBus logBus, SqliteVrcLogStore store, HostConfig hostConfig, ModuleManager modules, NotificationManager notify,
    ModuleConfigStore configs, Localizer localizer, StatusBoard status,
    AppLogSink appLog, UiToken uiToken, ContextMenus menus, ShortcutBoard shortcuts, ModuleCatalog catalog, ModuleInstallJobs jobs,
    SettingsApplier applier, ILogger logger)
{
    /// <summary>一度に読み戻す上限。500 では足りない — 記録の 92% が <c>ApiRequest</c>（既定で畳む種別）なので、500 行取っても見える行は 10 件ほどしか無く、半日ぶんも遡れない。</summary>
    private const int MaxQueryRows = 5000;

    /// <summary>アプリログの遡り 1 回の既定の行数。画面の打ち切り（MAX_ROWS）と同じ。</summary>
    private const int AppLogPageSize = 500;

    /// <summary>token を確かめる。公開メソッドは全部ここを通す — 通らないメソッドが 1 つでもあれば、モジュールの iframe からそこだけ叩ける。抜けは試験（<c>UiBridgeGuardTests</c>）が見張る。</summary>
    private void Authorize(string token, [CallerMemberName] string method = "")
    {
        if (uiToken.Matches(token)) return;

        // 黙って失敗させると boot() が止まった理由が分からないので、必ず残す。
        logger.LogWarning("Refused a call to {Method} with no token", method);

        throw new UnauthorizedAccessException($"{method}: callable only from the shell");
    }

    /// <summary>UI の初期化時に呼ばれる。push を取りこぼしても、これで現在値に追いつける。</summary>
    public object GetStatus(string token)
    {
        Authorize(token);

        return new
        {
            version = Version,
            logFile = logBus.CurrentLogFile is { } path ? Path.GetFileName(path) : null,
            hiddenKinds = hostConfig.HiddenKinds,

            // 設定画面で畳んであるカテゴリ。設定の材料（getConfigs）には混ぜない — 畳みはモジュールごとの設定ではなく画面全体の状態で、hiddenKinds と同じくホストが 1 か所で持っている。
            collapsedCategories = hostConfig.CollapsedCategories,

            // 生行を遡れるかどうか。保存していない生行はそのセッション限りなので、画面は「遡りに混ぜるか」をこれで決める。
            storeAllLines = hostConfig.StoreAllLines,

            // API の呼び出しを残しているか。切のあいだは新しいものが届かないので、画面は溜まっている分も出さず、絞り込みの「API」を押せない形にする。
            storeApiRequests = hostConfig.StoreApiRequests,

            // 出ている途中のトースト。push は受け取れない間は捨てられるので、読み込み直しや後から繋いだ画面はここで追いつく（進み具合が片方だけ消えるのを防ぐ）。
            toasts = notify.LiveToasts(),

            // モジュールの読み込みは画面より先に走るので、そこで出たバナーは push では届いていない。ここで拾えないと、失敗が誰にも見えないまま終わる。
            banners = notify.LiveBanners(),

            // 状態の口（サイドバーの行の右・タイトルバー）。モジュールは画面より先に並ぶので、push では届いていない。
            status = status.ForBridge(),

            // 手首の帯の分。サイドバーとは別に絞る（「手首に出さないもの」）。
            wristStatus = status.ForBridge(wrist: true),

            // 答えを待っている確認と、このセッションの通知の履歴（タイトルバーのベル）。確認は push だけだと、出たあとに開いた面に答える場所が無い。
            asks = notify.LiveAsks(),
            history = notify.History(),

            wristShortcuts = hostConfig.WristShortcuts,

            wristHiddenNotes = hostConfig.HiddenWristNotes,

            // ソフトウェア自身のログ。モジュールの読み込みも CEF の起動も画面より先なので、起動でいちばん知りたい行ほど push では届いていない。ここで台帳から追いつく。
            appLogs = appLog.ForBridge(),

            appLogFile = appLog.Path is { } appLogPath ? Path.GetFileName(appLogPath) : null,
        };
    }

    /// <summary>アプリログを遡る（「アプリ」の面を下へ辿ったとき）。台帳ではなく記録から読むので、台帳から押し出された行も、前の起動の行も出る。</summary>
    /// <param name="requestJson"><c>{"beforeSeq": 数, "limit": 数}</c>。JSON 文字列で受ける理由は <see cref="SetHiddenKinds"/> と同じ。名前は小文字のまま読む（既定の JSON 設定は大文字小文字を区別するので、型へ束縛すると黙って既定値になる）。</param>
    public object QueryAppLogs(string token, string requestJson)
    {
        Authorize(token);

        long beforeSeq = 0;
        var limit = AppLogPageSize;
        try
        {
            if (JsonNode.Parse(requestJson) is JsonObject request)
            {
                beforeSeq = request["beforeSeq"]?.GetValue<long>() ?? 0;
                limit = Math.Clamp(request["limit"]?.GetValue<int>() ?? AppLogPageSize, 1, MaxQueryRows);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            logger.LogWarning("An app log page request could not be read: {Request}", requestJson);
        }

        if (beforeSeq <= 0) return new { rows = Array.Empty<object>(), hasMore = false };

        var (rows, hasMore) = appLog.Older(beforeSeq, limit);

        // 画面は下へそのまま繋ぐので、新しい順で渡す（記録は古い順で返す）。
        return new { rows = rows.Reverse().Select(AppLogSink.ToBridge).ToArray(), hasMore };
    }

    /// <summary>表示を畳む種別を差し替える。ホスト本体の設定に持つのは、デスクトップ窓と VR オーバーレイで同じ見え方にするため（片方だけが知っている状態を作らない）。画面はログ画面のチップで、設定画面には出さない（読みながら切り替えるものなので、設定画面へ回ると操作が遠くなる）。</summary>
    /// <param name="kindsJson">種別名の配列を JSON 文字列で受ける。配列をそのまま受けてはいけない — CefSharp は JS の配列を <c>List&lt;object&gt;</c> で渡すので、<c>string[]</c> の引数には束縛できず「Object of type List&lt;object&gt; cannot be converted to String[]」で落ちる。JS 側は戻り値を待っていないため、この失敗は画面には何も出ない。構造のあるものは JSON 文字列で渡す、という <see cref="ApplyConfigChanges"/> と同じ約束に揃える。</param>
    public string[] SetHiddenKinds(string token, string kindsJson)
    {
        Authorize(token);

        string[] kinds;
        try
        {
            kinds = JsonSerializer.Deserialize<string[]>(kindsJson) ?? [];
        }
        catch (JsonException)
        {
            return hostConfig.HiddenKinds;
        }

        hostConfig.HiddenKinds = kinds;
        configs.Save(HostConfig.Id);
        return kinds;
    }

    /// <summary>設定画面で畳んであるカテゴリを差し替える。値は何も動かない — 畳みは見た目だけで、中の設定は畳んでも今までどおり効いている。畳む / 開くは画面がその場で効かせ、ここは覚えるためだけに呼ばれるので、画面は戻り値を待たない。</summary>
    /// <param name="categoriesJson"><c>"モジュール:宛先"</c> の配列を JSON 文字列で受ける。配列をそのまま受けてはいけない理由は <see cref="SetHiddenKinds"/> と同じ（CefSharp が <c>List&lt;object&gt;</c> で渡すので束縛できず、しかも画面は戻り値を待たないので失敗が何も見えない）。</param>
    public string[] SetCollapsedCategories(string token, string categoriesJson)
    {
        Authorize(token);

        string[] categories;
        try
        {
            categories = JsonSerializer.Deserialize<string[]>(categoriesJson) ?? [];
        }
        catch (JsonException)
        {
            return hostConfig.CollapsedCategories;
        }

        hostConfig.CollapsedCategories = categories;
        configs.Save(HostConfig.Id);
        return categories;
    }

    /// <summary>記録から 1 ページ引く。追いつきも遡りもここを通る。組み立ては <see cref="LogPage"/> に置いてある（token と CefSharp の束縛が要る場所でしか試験できなくなるため）ので、この窓口は「token を確かめて渡す」だけに留める。</summary>
    public async Task<object> QueryLog(string token, string requestJson)
    {
        Authorize(token);

        return await LogPage
            .BuildAsync(store, LogPage.Parse(requestJson, logger), MaxQueryRows, logger)
            .ConfigureAwait(false);
    }

    /// <summary>パネルを持たないモジュールも動作確認のため出す。</summary>
    public object[] GetModules(string token)
    {
        Authorize(token);

        return
        [
            .. modules.Modules
            .Where(p => p.State == ModuleState.Running)
            .Select(p => new
            {
                id = p.Id,
                name = p.Instance.DisplayName,
                author = p.Instance.Author,
                icon = p.Instance.Icon,

                // 線のアイコン。パスの字だけ通す — シェルの origin の属性に、モジュールの書いた字が入るため。
                iconPath = NotifyKind.IsSafePath(p.Instance.IconPath) ? p.Instance.IconPath : null,
                hasUi = p.Ui.Endpoint is not null,

                // パネルの入口。実体が無いものはホスト側で null に倒してある。
                    panel = p.PanelEntry is { } entry ? $"{SpoitableSchemeHandlerFactory.SchemeName}://{p.Id}/{entry}" : null,

                    // VR の空間の窓で出すページ。無ければ空間の窓でも panel を出す（シェルが選ぶ）。
                    vrPanel = p.VrPanelEntry is { } vr ? $"{SpoitableSchemeHandlerFactory.SchemeName}://{p.Id}/{vr}" : null,
                }),
        ];
    }

    /// <summary>
    /// モジュール宛の呼び出しを中継する（envelope のホスト側）。シェルが <c>postMessage</c> の発信元 origin からモジュール Id を決め、ここへ渡す。モジュールごとにオブジェクトを bind しないのは、ホスト側の窓口を 1 箇所に保つため（ログも検査もここだけ見ればよい）。
    /// bind がブラウザ単位なのでモジュールの iframe からも <c>stkHost</c> には届くが、シェルだけが持つ token（<see cref="UiToken"/>）で塞いである。
    /// 失敗は例外にせず <c>ok=false</c> で返す（片方のモジュールの失敗で UI 全体の呼び出し経路が壊れたように見えるのを避けるため）。
    /// </summary>
    public async Task<object> CallModule(string token, string module, string method, string? payload)
    {
        Authorize(token);

        try
        {
            var result = await modules.CallUiAsync(module, method, payload ?? string.Empty).ConfigureAwait(false);
            return new { ok = true, result };
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message };
        }
    }

    /// <summary>設定画面の材料。モジュールごとにまとめて返す。画面は毎回ここから作り直す。項目の定義も値も C# 側にあるので、2 つのサーフェス（デスクトップ窓 / VR オーバーレイ）で見え方がずれない。</summary>
    public object[] GetConfigs(string token)
    {
        Authorize(token);

        // Windows の「スタートアップ アプリ」で切られていたら、画面に出す前に設定へ写す（出す値が Windows の状態と食い違わないように）。
        Startup?.Reconcile();

        return configs.Describe(localizer);
    }

    /// <summary>Windows の起動時の起動。窓口より後に作られることがあるので、後から挿す（<see cref="Updates"/> と同じ）。</summary>
    internal WindowsStartup? Startup;

    /// <summary>画面の文言。シェルもモジュールのパネルも、これを 1 回受け取って自分で引く（1 語ずつ往復すると、描くたびに何十回も橋を渡ることになる）。</summary>
    public object GetStrings(string token)
    {
        Authorize(token);

        return new
        {
            language = localizer.Language,

            // JSON 文字列で渡す。辞書をそのまま返すと CefSharp が JS へ渡せない（getConfigs の値と同じ約束）。
            strings = JsonSerializer.Serialize(localizer.Snapshot()),
        };
    }

    /// <summary>まだ適用していない変更を当てたつもりで設定の材料を取り直す。入れ子の設定（<see cref="ISpoitableSettings"/>）は、出す / 出さないをモジュールの式が決め、画面はその式を評価できないので、スイッチを押すたびにここへ聞き直す。保存も通知もしない。</summary>
    public object[] PreviewConfigs(string token, string changesJson)
    {
        Authorize(token);

        // 読めない形なら今の値で描く。打ちかけで画面が消えるより、動かない方がまし。
        var pending = JsonNode.Parse(changesJson) as JsonObject ?? [];

        return configs.DescribeWith(pending, localizer);
    }

    /// <summary>
    /// 設定画面の「適用」。<c>{ "モジュール": { "保存名": 値 } }</c> という形の JSON を受ける。
    /// まとめて受けるのが肝: 入力のたびに書くと、関係する設定を 2 つ変えただけでモジュール側の作り直しが 2 回走る。モジュールへの通知は 1 回にまとめる。
    /// モジュール単位で全部か何も無しか。1 つでも読めない値があれば、そのモジュールは何も変えない。失敗しても設定画面ごと壊さず、どれが駄目だったかを返す（<see cref="CallModule"/> と同じ扱い）。
    /// </summary>
    public object ApplyConfigChanges(string token, string changesJson)
    {
        Authorize(token);

        List<object> notes = [];
        List<object> applied = [];
        var ok = true;
        var language = hostConfig.Language;

        try
        {
            if (JsonNode.Parse(changesJson) is not JsonObject root)
                throw new ArgumentException("The change has the wrong shape");

            foreach (var (module, values) in root)
            {
                if (values is not JsonObject changes || changes.Count == 0) continue;

                try
                {
                    applier.Apply(module, changes);
                }
                catch (ConfigRejectedException ex)
                {
                    // 1 つのモジュールで失敗しても、他のモジュールの適用は続ける。
                    // 値ごとに断られたときは、入った欄（保存済み）も返す。画面はそれを溜めから外し、断られた欄だけ残す。
                    ok = false;
                    Collect(module, ex.Notes);
                    foreach (var key in ex.Applied) applied.Add(new { module, key });
                }
                catch (Exception ex)
                {
                    ok = false;
                    Collect(module, [new ConfigNote(null, ex.Message)]);
                }
            }
        }
        catch (Exception ex)
        {
            ok = false;
            Collect(null, [new ConfigNote(null, ex.Message)]);
        }

        // 言語が変わったら辞書を読み直してから、全部の面へ流す（片方の画面だけ古い値のまま、を作らない）。
        // 読み込み直しは前の読み込み中だと無視されるので、保存のたびに全パネルを読み込み直すと、続けて保存したときパネルがテーマの当たっていない途中の状態で取り残される。
        // テーマは張り直しの道で追いつくので、読み込み直しは言語のときだけでよい。
        applier.Announce(language);

        // 「サイドバーに出さない状態」を変えたときは、並ぶ中身が変わるので、次の周（StatusBoard.Flush）で配られる。

        // 押した手応え。成功したときだけ出す（失敗のときは注記が出る）。答えを待つ話ではないので一番軽い側に置く — 設定を続けて直す邪魔をしない。
        if (ok) notify.PushToast(NotifyKind.Done, localizer["host.settings.saved"]);

        // record ではなく匿名型で返す。record は EqualityContract（System.Type）を持つので、CefSharp の bind 越しに渡すと壊れる（呼び出しが成功扱いにならない）。
        return new { ok, notes = notes.ToArray(), applied = applied.ToArray() };

        void Collect(string? module, IReadOnlyList<ConfigNote> from)
        {
            foreach (var note in from)
                notes.Add(new { module, key = note.Key, message = note.Message });
        }
    }

    /// <summary>
    /// 設定画面の動きの行（<see cref="SpoitableSettingsAction"/>）のボタン。「適用」とは別で、押した瞬間に走る。
    /// <b>待たない</b> — 走らせるのは裏で、行の姿（走っている間はボタンを押せない）は <c>configAction</c> の push で全部の面へ届く。
    /// 走っている間の押し・結ばれていない行・無い行は何もしない（false）。
    /// </summary>
    public bool RunConfigAction(string token, string module, string key)
    {
        Authorize(token);

        return configs.RunAction(module ?? string.Empty, key ?? string.Empty);
    }

    /// <summary>通知への回答。どのサーフェスから答えても同じ口へ来る（先に答えた方が効く）。</summary>
    public void AnswerNotification(string token, long id, bool accepted)
    {
        Authorize(token);

        notify.Answer(id, accepted);
    }

    /// <summary>トーストを閉じる。時計と利用者のどちらが先でも構わない（ホスト側で 1 回だけ効く）。</summary>
    public void CloseToast(string token, long id)
    {
        Authorize(token);

        notify.CloseToast(id);
    }

    /// <summary>確認の「今後は聞かない」。答えは「承諾されなかった」で閉じ、モジュールの取り決めが走る。</summary>
    public void OptOutNotification(string token, long id)
    {
        Authorize(token);

        notify.OptOut(id);
    }

    /// <summary>時間切れで答えそびれた確認に、通知の履歴から後で答える。1 回だけ効く。</summary>
    public void AnswerNotificationLate(string token, long seq)
    {
        Authorize(token);

        notify.AnswerLate(seq);
    }

    /// <summary>VR へ頼む口。窓口より後に出来るので後から挿す（<see cref="Window"/> と同じ）。SteamVR を使わない起動や <c>SPOITABLE_NO_VR</c> では null のまま — そのときは何もしない。中身は <see cref="VrSlot"/>（モジュールの <c>host.Ui.ToggleVrPanel</c> と同じ差し込み口）。</summary>
    internal IVrControls? Vr
    {
        get => VrSlot.Current;
        set => VrSlot.Current = value;
    }

    /// <summary>VR へ頼む口の差し込み口。Program がモジュールと同じものを渡す。既定の値を持たせない（<see cref="ModuleManager.Vr"/> と同じ理由: 渡し忘れが VR でだけ表に出る）。</summary>
    internal required VrControlsSlot VrSlot { get; init; }

    /// <summary>手首のショートカット。空間の窓をその画面で開く。その画面を出しているなら閉じ、別の画面なら切り替える。ダッシュボードは SteamVR のメニューから今までどおり開ける。</summary>
    public void ToggleVrWindow(string token, string view)
    {
        Authorize(token);

        Vr?.ToggleWindow(view);
    }

    /// <summary>手首のショートカットに並べられるもの全部（本体・パネルを持つモジュール・モジュールが名乗ったもの）。台帳の順。手首のタイルと設定のモーダルが引く。中身（<c>text</c> / <c>state</c>）のその後の変化は <c>shortcutStatus</c> の push で来る。</summary>
    public object[] GetShortcuts(string token)
    {
        Authorize(token);

        return shortcuts.ForBridge();
    }

    /// <summary>手首のタイルが押された。開くものは <see cref="ToggleVrWindow"/> と同じ（その画面なら閉じる）、処理は裏で走らせて待たない。名前は台帳で引き直すので、画面が渡した名前が台帳に無ければ何もしない。</summary>
    public void PressShortcut(string token, string key)
    {
        Authorize(token);

        _ = shortcuts.PressAsync(key, view => Vr?.ToggleWindow(view));
    }

    /// <summary>
    /// 手首のタイル（モジュールが登録したショートカット）を押した・動かした・離した・押しが途切れた。押したか引いたかは C# が決める（<see cref="ShortcutGestures"/>）。
    /// <paramref name="seq"/> は手首のページが押すたびに増やす番号（同じ押しの動き・離しも同じ番号）で、<paramref name="page"/> はそのページの読み込みの印（番号は読み込みごとに 1 から数え直す）。位置はタイルの左上からの画素と、タイルの大きさ。
    /// 名前も値も画面から来たものなので、決める所でもう一度検める。
    /// </summary>
    public void ShortcutPointer(string token, string page, string key, long seq, string kind, double x, double y, double width, double height)
    {
        Authorize(token);

        ShortcutPointerKind? parsed = kind switch
        {
            "down" => ShortcutPointerKind.Down,
            "move" => ShortcutPointerKind.Move,
            "up" => ShortcutPointerKind.Up,
            "cancel" => ShortcutPointerKind.Cancel,
            _ => null,
        };

        if (parsed is not { } pointer || key is null)
        {
            logger.LogWarning("The wrist sent a pointer of an unknown kind ({Kind}) on {Key}; ignoring it", kind, key);
            return;
        }

        Gestures.Pointer(page, key, seq, pointer, (float)x, (float)y, (float)width, (float)height);
    }

    internal required ShortcutGestures Gestures { get; init; }

    public void CloseVrWindow(string token)
    {
        Authorize(token);

        Vr?.CloseWindow();
    }

    /// <summary>空間の窓のページが自分で画面を切り替えた（サイドバー）。窓のページだけが呼ぶ（ダッシュボードと窓の browser は呼ばない）。</summary>
    public void VrWindowViewing(string token, string view)
    {
        Authorize(token);

        Vr?.WindowViewing(view);
    }

    /// <summary>空間の窓がいま出しているはずの画面（閉じていれば null、VR が無ければ null）。窓のページが読み終わったときに聞き直す。窓の browser を作った直後に別のショートカットが押されると、その切り替え（<c>showPanel</c> の push）は読み込みの途中で捨てられる。聞き直さないと、C# は切り替えたつもりで窓は前の画面のまま — 同じボタンをもう一度押すと閉じてしまう。</summary>
    public string? GetVrWindowView(string token)
    {
        Authorize(token);

        return Vr?.WindowView;
    }

    /// <summary>Layout Mode か（VR が無ければ false）。空間の窓のページが読み終わったときに聞き直す（切り替えは <c>vrLayout</c> の push で来るが、作った直後・読み込み中のページには届かない。<see cref="GetVrWindowView"/> と同じ理由）。</summary>
    public bool GetVrLayout(string token)
    {
        Authorize(token);

        return Vr?.InLayout ?? false;
    }

    public void ResetWristPosition(string token)
    {
        Authorize(token);

        Vr?.ResetWristPosition();
    }

    /// <summary>手首の板の Layout Mode に入る / 出る。今どちらかは C# が決めて、画面へ送り返す（<c>wristLayout</c>）。</summary>
    public void SetWristLayout(string token, bool on)
    {
        Authorize(token);

        Vr?.SetWristLayout(on);
    }

    /// <summary>手首の板の大きさを刻みで変える（負で小さく）。範囲は手首の側が丸める（10〜40 cm）。</summary>
    public void ResizeWrist(string token, int steps)
    {
        Authorize(token);

        Vr?.ResizeWrist(steps);
    }

    /// <summary>バナーを閉じる。消せるのは利用者だけなので、入口はここしかない。</summary>
    public void CloseBanner(string token, long id)
    {
        Authorize(token);

        notify.CloseBanner(id);
    }

    public void InvokeBannerAction(string token, long id, int index)
    {
        Authorize(token);

        notify.InvokeBannerAction(id, index);
    }

    /// <summary>トーストの押せるものが押された。走らせるのはホスト側の裏スレッドで、ここは戻り値を待たない（モジュールの処理が画面を止めないため）。</summary>
    public void InvokeToastAction(string token, long id, int index)
    {
        Authorize(token);

        notify.InvokeToastAction(id, index);
    }

    /// <summary>窓を動かす口。窓より先にこの窓口が作られるので、後から挿す。VR のサーフェスには窓が無いので null のままになりうる — そのときは何もしない。</summary>
    internal IHostWindow? Window;

    public void MinimizeWindow(string token)
    {
        Authorize(token);

        Window?.Minimize();
    }

    /// <summary>最大化と元のサイズを行き来する。切り替えた後の状態を返す（画面はボタンの絵を変えたいので、状態を聞き直す往復を省く）。</summary>
    public bool ToggleMaximizeWindow(string token)
    {
        Authorize(token);

        if (Window is not { } window) return false;

        window.ToggleMaximize();
        return !window.IsMaximized;
    }

    public void CloseWindow(string token)
    {
        Authorize(token);

        Window?.CloseWindow();
    }

    /// <summary>
    /// 閉じるときの問い（閉じるボタンの動きを決めていないときに出る）の答え。<c>tray</c> か <c>exit</c>。答えを設定に書いてから、その動きをする。
    /// ほかの字は何もしない（false）。問いを閉じただけ（Esc・外を押した）なら、画面はこれを呼ばない（設定は聞くのまま）。
    /// </summary>
    public bool AnswerClose(string token, string choice)
    {
        Authorize(token);

        CloseButtonAction? chosen = choice switch
        {
            "tray" => CloseButtonAction.Tray,
            "exit" => CloseButtonAction.Exit,
            _ => null,
        };
        if (chosen is not { } action || Window is not { } window) return false;

        window.AnswerClose(action);
        return true;
    }

    /// <summary>管理画面の「再起動」。窓口が出来てから挿す（<see cref="Window"/> と同じ）。</summary>
    internal AppRestart? Restart;

    /// <returns><see cref="ModuleCatalogView"/> の JSON（<c>modulesChanged</c> で配るものと同じ形・同じ作り方）。</returns>
    public string GetModuleCatalog(string token)
    {
        Authorize(token);
        return UiBroadcast.Json(catalog.Snapshot());
    }

    /// <summary>次の起動から読むか。受け付けなくても今の中身を返す（画面は返った方を描く）。</summary>
    /// <returns><see cref="ModuleCatalogView"/> の JSON。</returns>
    public string SetModuleEnabled(string token, string id, bool on)
    {
        Authorize(token);
        catalog.SetEnabled(id, on);
        return UiBroadcast.Json(catalog.Snapshot());
    }

    /// <returns><see cref="ModuleCatalogView"/> の JSON。</returns>
    public string SetModuleRemoval(string token, string id, bool remove)
    {
        Authorize(token);
        catalog.SetRemoval(id, remove);
        return UiBroadcast.Json(catalog.Snapshot());
    }

    /// <returns><see cref="ModuleCatalogView"/> の JSON。</returns>
    public string CancelPendingModule(string token, string id)
    {
        Authorize(token);
        catalog.CancelPending(id ?? string.Empty);
        return UiBroadcast.Json(catalog.Snapshot());
    }

    /// <summary>「モジュールを追加」の URL の段。その URL で件を始められるかと、同意の段を出すかを答える（件は作らない。<see cref="ModuleInstallJobs.Check"/>）。</summary>
    /// <returns><see cref="ModuleUrlCheck"/> の JSON。</returns>
    public string CheckModuleUrl(string token, string url)
    {
        Authorize(token);
        return UiBroadcast.Json(jobs.Check(url ?? string.Empty));
    }

    /// <summary>同意の段の「今後この画面を表示しない」。本体の設定 <see cref="HostConfig.ShowModuleAgreement"/> を切って保存する。</summary>
    public void HideModuleAgreement(string token)
    {
        Authorize(token);
        hostConfig.ShowModuleAgreement = false;
        configs.Save(HostConfig.Id);
        logger.LogInformation("The module agreement will not be shown again (it can be turned back on in the settings)");
    }

    /// <summary>モジュールを入れ始める（URL を調べる所から）。入れている途中のものは本体が持ち、変わるたびに <c>moduleJobs</c> で全部の面へ配る（<see cref="ModuleInstallJobs"/>）。同じ repo の件がもうあれば、それを返す。</summary>
    /// <returns><see cref="ModuleJobStart"/> の JSON（<see cref="UiBroadcast.Json"/>）。</returns>
    public string StartModuleJob(string token, string url)
    {
        Authorize(token);
        return UiBroadcast.Json(jobs.Start(url ?? string.Empty));
    }

    /// <returns><see cref="ModuleJobsView"/> の JSON（<c>moduleJobs</c> で配るものと同じ形・同じ作り方）。</returns>
    public string GetModuleJobs(string token)
    {
        Authorize(token);
        return UiBroadcast.Json(jobs.Snapshot());
    }

    /// <summary>その件が聞いていることに「はい」と答える（build する・.NET SDK を落とす・入れる）。<paramref name="ask"/> は聞かれた質問の通し番号、<paramref name="kind"/> は何の質問か。いま聞いている質問と違えば通らない（画面からは repo も commit も URL も受け取らない。何に同意したかは、本体がその件に持っている）。</summary>
    public bool AnswerModuleJob(string token, string id, int ask, string kind)
    {
        Authorize(token);
        return jobs.Answer(id ?? string.Empty, ask, kind ?? string.Empty);
    }

    /// <summary>その件を止める（進んでいるもの）・閉じる（失敗して残っているもの）。</summary>
    public bool CancelModuleJob(string token, string id)
    {
        Authorize(token);
        return jobs.Cancel(id ?? string.Empty);
    }

    public bool RetryModuleJob(string token, string id)
    {
        Authorize(token);
        return jobs.Retry(id ?? string.Empty);
    }

    /// <summary>入れ元に新しい版があるかを、いま確かめる（押したとき。1 日 1 回の決まりは通さない）。<c>ok</c> が false なら、答えの返らなかった入れ元がある（画面は「更新はありません」と言わない）。</summary>
    /// <returns><see cref="ModuleUpdateCheckView"/> の JSON。</returns>
    public async Task<string> CheckModuleUpdates(string token)
    {
        Authorize(token);
        var check = await catalog.CheckUpdatesAsync(force: true, CancellationToken.None).ConfigureAwait(false);
        return UiBroadcast.Json(new ModuleUpdateCheckView(check.Complete, catalog.Snapshot()));
    }

    private readonly LicenseTexts _licenses = new(AppContext.BaseDirectory, logger);

    public string GetLicenses(string token)
    {
        Authorize(token);
        return UiBroadcast.Json(_licenses.Read());
    }

    public bool OpenChromiumCredits(string token)
    {
        Authorize(token);
        if (Window is not { } window) return false;

        window.OpenChromiumCredits(localizer["host.licenses.chromium"]);
        return true;
    }

    /// <summary>起動し直す。新しいアプリを立てられなければ false（この窓は閉じない）。</summary>
    public bool RestartApp(string token)
    {
        Authorize(token);
        return Restart?.Request() ?? false;
    }

    /// <summary>自前のタイトルバーを掴んだとき。押した瞬間に 1 回だけ呼ぶ — 以降の追従は Windows がやるので、マウスの動きを橋渡ししない。</summary>
    public void BeginWindowDrag(string token)
    {
        Authorize(token);

        Window?.BeginDrag();
    }

    /// <summary>クリップボードへ書く。JS の <c>navigator.clipboard</c> は独自スキームだと使えないことがあるので、ホスト側に置く。</summary>
    public bool CopyToClipboard(string token, string text)
    {
        Authorize(token);

        return HostClipboard.SetText(text);
    }

    /// <summary>
    /// 右クリックされた場所に出す項目。名乗りは画面から来る（CEF が渡してくるものには「どの要素か」が入っていないので、何の上で押されたかはページにしか分からない。<see cref="ContextMenus"/> を見ること）。
    /// 空の配列が返ったら、画面はメニューを開かない。何も無い背景で右クリックしたときに空の箱が出ないようにするのは画面側の仕事で、ここは「出すものが無い」とだけ言う。
    /// </summary>
    public object[] GetContextMenu(string token, string requestJson)
    {
        Authorize(token);

        var request = ContextMenuRequest.Parse(requestJson, logger);

        return
        [
            .. menus.Build(request.ToTarget(), request.Module)
                .Select(item => new { id = item.Id, labelKey = item.LabelKey, enabled = item.Enabled, separator = item.Separator }),
        ];
    }

    /// <summary>押された項目を実行する。返りの <c>act</c> はページ側に残った仕事（切り取り・貼り付けは DOM を書き換えないと終わらない）。無ければページは何もしなくてよい。</summary>
    public async Task<object> InvokeContextMenu(string token, string requestJson)
    {
        Authorize(token);

        var request = ContextMenuRequest.Parse(requestJson, logger);
        var result = await menus.InvokeAsync(request.Id, request.ToTarget(), request.Module).ConfigureAwait(false);

        return new { ok = result.Ok, act = result.Act, text = result.Text };
    }

    /// <summary>窓口より後に作られることがあるので、後から挿す（<see cref="Window"/> と同じ）。</summary>
    internal AppUpdates? Updates;

    public string GetAppUpdate(string token)
    {
        Authorize(token);
        return UiBroadcast.Json(Updates?.View() ?? AppUpdates.Unavailable);
    }

    public async Task<string> CheckAppUpdate(string token)
    {
        Authorize(token);
        if (Updates is not { } updates) return UiBroadcast.Json(AppUpdates.Unavailable);
        return UiBroadcast.Json(await updates.CheckAsync(manual: true, CancellationToken.None).ConfigureAwait(false));
    }

    public string InstallAppUpdate(string token)
    {
        Authorize(token);
        return UiBroadcast.Json(Updates?.Install() ?? AppUpdates.Unavailable);
    }

    public string SkipAppUpdate(string token)
    {
        Authorize(token);
        return UiBroadcast.Json(Updates?.Skip() ?? AppUpdates.Unavailable);
    }

    public string GetAppUpdateNotes(string token)
    {
        Authorize(token);
        return UiBroadcast.Json(Updates?.Notes(localizer.Language) ?? new AppUpdateNotesView(null, []));
    }

    private static string Version => AppVersion.Text;
}
