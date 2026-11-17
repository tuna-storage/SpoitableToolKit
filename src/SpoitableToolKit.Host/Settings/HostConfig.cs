using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Config.Attributes;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Config.Attributes;
using SpoitableToolKit.Host.Notifications;
using SpoitableToolKit.Host.Status;
using SpoitableToolKit.Host.Theming;
using SpoitableToolKit.Host.Ui.Shell;
using SpoitableToolKit.Host.VRChat;

namespace SpoitableToolKit.Host.Settings;

/// <summary>ホスト本体の設定。モジュールと同じ仕組みに乗せるので、設定画面には本体もモジュールも同じように並ぶ。保存先はデータディレクトリ直下の <c>config.json</c>。</summary>
internal sealed class HostConfig : IModuleConfig
{
    // 並ぶ順がそのまま画面の順。束ね（SpoitableSettingsGroup）1 つが設定画面の左の一覧の 1 ページになる。
    // 束ねは見せ方だけで、保存は平らなまま（入れ子にすると config.json の形が変わって、打った値が読めなくなる）。

    [SpoitableSettingsGroup("host.settings.group.display")]
    [SpoitableSettingsLabelLocalize("host.settings.language.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.language.description")]
    public UiLanguage Language = UiLanguage.Auto;

    /// <summary>設定画面と保存で使うホスト自身の Id。モジュールの Id と衝突しない名前にしてある。</summary>
    public const string Id = "host";

    /// <summary>配色。<c>themes/&lt;名前&gt;/theme.css</c> を引く。候補であって選択肢ではないので、一覧に無い名前も打てる（打った先が無ければ既定に落ちて、ログに残る）。</summary>
    [SpoitableSettingsGroup("host.settings.group.display")]
    [SpoitableSettingsLabelLocalize("host.settings.theme.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.theme.description")]
    public string Theme = HostThemes.Default;

    /// <summary>
    /// サイドバーに出さない状態。<c>host</c>（タイトルバーの「ログを読んでいます」）か、
    /// 様子を登録した（<see cref="SpoitableToolKit.Abstractions.Ui.IModuleUi.RegisterStatus"/>）モジュールの Id。
    /// 既定は空で、出したものは全部並ぶ（選ばないと出ない形だと、モジュールを入れた人が設定を開くまで価値に気付けない）。
    /// </summary>
    [SpoitableSettingsGroup("host.settings.group.display")]
    [SpoitableSettingsFormerKey("HiddenFooterItems")]
    [SpoitableSettingsLabelLocalize("host.settings.hiddenSidebarStatus.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.hiddenSidebarStatus.description")]
    [SpoitableSettingsSwitches]
    public string[] HiddenSidebarStatus = [];

    /// <summary>読むのは行が来るたびなので、切り替えはすぐ効く（再起動は要らない）。</summary>
    [SpoitableSettingsGroup("host.settings.group.log")]
    [SpoitableSettingsLabelLocalize("host.settings.storeAllLines.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.storeAllLines.description")]
    public bool StoreAllLines;

    /// <summary>
    /// VRChat の API の呼び出し（<c>ApiRequest</c>）も残すか。切のあいだは保存も表示もしない
    /// （<see cref="LogRouting"/> で捨てる。バスの購読者 = モジュールには今までどおり届く）。
    /// 既定が切なのは、<c>vrclog.db</c> の出来事の大半（実測で 95%）が ApiRequest になり容量を圧迫するため。
    /// 切にしても溜まっている分は消さず、画面では入にするまで出さない。
    /// <c>[API]</c> の生行も、切のあいだは新しく来る分を保存も表示もしない（もう残っている生行はそのまま出る）。
    /// 読むのは出来事が来るたびなので、切り替えはすぐ効く（再起動は要らない）。
    /// </summary>
    [SpoitableSettingsGroup("host.settings.group.log")]
    [SpoitableSettingsLabelLocalize("host.settings.storeApiRequests.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.storeApiRequests.description")]
    public bool StoreApiRequests;

    /// <summary>
    /// アプリのログの記録（<c>applog.db</c>）を残す日数。起動したときと変えたときに古いものを消す（<see cref="AppLog.AppLogRetention"/>）。
    /// 人に送る用のファイル（<c>applog/</c>）は回数で間引くので、これとは別。
    /// </summary>
    [SpoitableSettingsGroup("host.settings.group.log")]
    [SpoitableSettingsLabelLocalize("host.settings.appLogKeepDays.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.appLogKeepDays.description")]
    [SpoitableSettingsRange(AppLog.AppLogRetention.MinDays, AppLog.AppLogRetention.MaxDays)]
    public int AppLogKeepDays = 30;

    [SpoitableSettingsGroup("host.settings.group.notify")]
    [SpoitableSettingsLabelLocalize("host.settings.confirmSeconds.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.confirmSeconds.description")]
    [SpoitableSettingsRange(5, 30)]
    public int ConfirmSeconds = 15;

    /// <summary>
    /// 答えを待たない通知（トースト）を出しておく時間。進み具合を持つトーストには効かない（終わりが定義されているものは、終わるまで消えない）。
    /// 押せるものが付いているトーストには下限がある（<see cref="NotificationManager"/>）。読んでから手を動かす時間は、見るだけの通知より要る。
    /// </summary>
    [SpoitableSettingsGroup("host.settings.group.notify")]
    [SpoitableSettingsLabelLocalize("host.settings.toastSeconds.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.toastSeconds.description")]
    [SpoitableSettingsRange(1, 60)]
    public int ToastSeconds = 3;

    [SpoitableSettingsGroup("host.settings.group.osc")]
    [SpoitableSettingsLabelLocalize("host.settings.oscPort.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.oscPort.description")]
    [SpoitableSettingsRange(1, 65535)]
    public int OscPort = 9000;

    /// <summary>
    /// 手首の帯（ショートカットバー）と VR ウィンドウを使うか。切り替えはその場で効く（VR の周ごとに読む: <see cref="Ui.Vr.VrFeatures"/>）。
    /// 切ると板も、その browser も片付ける。入れ直したら作り直す。
    /// <see cref="VrDashboard"/> と両方切ると SteamVR から手を放し、どちらかを入れるまで繋ぎに行かない（SteamVR を起こしうる呼び出しを何もしない）。
    /// </summary>
    [SpoitableSettingsGroup("host.settings.group.vr")]
    [SpoitableSettingsLabelLocalize("host.settings.vrOverlay.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.vrOverlay.description")]
    public bool VrOverlay = true;

    /// <summary>
    /// SteamVR のダッシュボードにこちらのパネル（タブ）を出すか。切り替えはその場で効く（<see cref="VrOverlay"/> と同じく周ごとに読む）。
    /// 切ってもダッシュボードそのものは SteamVR のもので、開けば手首の帯と VR ウィンドウは今までどおり隠れる。
    /// </summary>
    [SpoitableSettingsGroup("host.settings.group.vr")]
    [SpoitableSettingsLabelLocalize("host.settings.vrDashboard.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.vrDashboard.description")]
    public bool VrDashboard = true;

    /// <summary>VR の手首の板をどちらの手に付けるか。切り替えはその場で効く（周ごとに読む）。</summary>
    [SpoitableSettingsGroup("host.settings.group.vr")]
    [SpoitableSettingsLabelLocalize("host.settings.wristHand.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.wristHand.description")]
    public WristHand WristHand = WristHand.Left;

    /// <summary>
    /// 手首の板を出す角度（度）。板の真ん中が視野の真ん中（頭の向き）からこれより近くなったら出す（面が裏・真横を向いていたら出さない）。
    /// その場で効く（周ごとに読む）。
    /// 隠す角度（<see cref="WristHideBeyondDegrees"/>）と分けてあるのは、境目で手首を止めたときに出たり消えたりを繰り返さないため。
    /// 既定は <see cref="Ui.Vr.Wrist.WristPlacement.DefaultShowWithinDegrees"/>。
    /// </summary>
    [SpoitableSettingsGroup("host.settings.group.vr")]
    [SpoitableSettingsLabelLocalize("host.settings.wristShowWithinDegrees.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.wristShowWithinDegrees.description")]
    [SpoitableSettingsRange(Ui.Vr.Wrist.WristPlacement.MinDegrees, Ui.Vr.Wrist.WristPlacement.MaxDegrees)]
    public int WristShowWithinDegrees = Ui.Vr.Wrist.WristPlacement.DefaultShowWithinDegrees;

    /// <summary>
    /// 手首の板を隠す角度（度）。出ている板の真ん中が視野の真ん中からこれより離れたら隠す。
    /// 出す角度より小さくしても、出す角度として扱う（小さいと、出した次の周で隠れる）。
    /// </summary>
    [SpoitableSettingsGroup("host.settings.group.vr")]
    [SpoitableSettingsLabelLocalize("host.settings.wristHideBeyondDegrees.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.wristHideBeyondDegrees.description")]
    [SpoitableSettingsRange(Ui.Vr.Wrist.WristPlacement.MinDegrees, Ui.Vr.Wrist.WristPlacement.MaxDegrees)]
    public int WristHideBeyondDegrees = Ui.Vr.Wrist.WristPlacement.DefaultHideBeyondDegrees;

    /// <summary>手首に出さないもの。サイドバーとは別に選ぶ（手首は一目で見たいものだけに絞りたい）。候補はサイドバーと同じ。</summary>
    [SpoitableSettingsGroup("host.settings.group.vr")]
    [SpoitableSettingsLabelLocalize("host.settings.hiddenWristItems.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.hiddenWristItems.description")]
    [SpoitableSettingsSwitches]
    public string[] HiddenWristItems = [Id, InstanceTracker.VideoId, InstanceTracker.PeopleId, ClockId];

    /// <summary>
    /// 手首の通知の板に出さないもの。ログ画面の絞り込みとは別に覚える。答えを待つ通知はいつも出る（ここでは絞れない）。
    /// 名前は VRChat の出来事の種別（<c>PlayerJoined</c> など）・通知の種類（<c>done</c>、<c>hrs.exported</c> など）・アプリのレベル（<c>WRN</c> / <c>ERR</c> / <c>CRT</c>）。
    /// 既定で再生の開始とアプリの警告を出さない（数が多く、手首で読むものではない）。
    /// </summary>
    [SpoitableSettingsGroup("host.settings.group.vr")]
    [SpoitableSettingsLabelLocalize("host.settings.hiddenWristNotes.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.hiddenWristNotes.description")]
    [SpoitableSettingsSwitches]
    public string[] HiddenWristNotes = ["VideoPlaybackStarted", "WRN"];

    /// <summary>手首の帯の時刻（本体が出す状態の口の 1 つ）。</summary>
    internal const string ClockId = "host.clock";

    private static readonly string[] WristNoteKinds =
    [
        "PlayerJoined", "PlayerLeft", "WorldJoined", "VideoPlaybackStarted", "VideoPlaybackFailed", "VideoResolved",
        NotifyKind.Info.Id, NotifyKind.Done.Id, NotifyKind.Warn.Id, NotifyKind.Error.Id, NotifyKind.Download.Id,
        VRChatPresence.NoteKind.Id,
        "WRN", "ERR", "CRT",
    ];

    /// <summary>
    /// 手首に並べるショートカット。押すと空間の窓がその画面で開く（開いている画面のをもう一度押すと閉じる）。
    /// <c>log</c> / <c>settings</c> / パネルを持つモジュールの Id。入っていないモジュールの Id は黙って並ばない。
    /// </summary>
    [SpoitableSettingsGroup("host.settings.group.vr")]
    [SpoitableSettingsLabelLocalize("host.settings.wristShortcuts.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.wristShortcuts.description")]
    [SettingsEditor(SettingsEditorAttribute.WristShortcuts)]
    public string[] WristShortcuts = ["hrs", "videourl"];

    /// <summary>
    /// Layout Mode（手首の 🔓。手首と空間の窓で共通）で、どの板も指さず・掴んでもいない時間がこの秒数続いたら固定に戻す。0 で戻さない（完了を押すまで）。
    /// 完了を押し忘れたまま次に手首を見ると、取っ手が出ていて間違って掴める。しかも Layout Mode の間はコントローラーの入力を全部預かる（VRChat に届かない）。
    /// 既定は 10 秒 — 腕を下ろして見え方を確かめる程度なら収まる。窓を並べている間は手首を下ろしているので、指している・掴んでいる間は数えない。
    /// </summary>
    [SpoitableSettingsGroup("host.settings.group.vr")]
    [SpoitableSettingsLabelLocalize("host.settings.wristLayoutLockSeconds.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.wristLayoutLockSeconds.description")]
    [SpoitableSettingsRange(0, 600)]
    public int WristLayoutLockSeconds = 10;

    /// <summary>
    /// 空間の窓を手首に付けるか、空間に置くか。既定は手首の板の上に付けて腕と一緒に動く。
    /// 目の前を選ぶと、開くたびに目の前の空間に置く。次に開いたときから効く。
    /// </summary>
    [SpoitableSettingsGroup("host.settings.group.vr")]
    [SpoitableSettingsLabelLocalize("host.settings.vrWindowSpot.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.vrWindowSpot.description")]
    public VrWindowSpot VrWindowSpot = VrWindowSpot.AboveWrist;

    /// <summary>
    /// 読み込まないモジュール（Id の一覧）。設定画面には出さない — モジュールの管理画面の ON/OFF がここに書く
    /// （<see cref="SpoitableToolKit.Host.Modules.Management.ModuleCatalog"/>）。効くのは次の起動から。
    /// </summary>
    [SpoitableSettingsHidden]
    public string[] DisabledModules = [];

    /// <summary>
    /// 「モジュールを追加」で URL を入れたあと、件を始める前に同意の段を出すか（インストールは自己責任・このソフトの作者は責任を負わない）。
    /// 同意の段の「今後この画面を表示しない」で切れる（<see cref="Ui.Shell.UiBridge.HideModuleAgreement"/>）。また出すには、ここを入にする。
    /// 行の「更新」からは、入でも出さない。
    /// </summary>
    [SpoitableSettingsGroup("host.settings.group.modules")]
    [SpoitableSettingsLabelLocalize("host.settings.showModuleAgreement.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.showModuleAgreement.description")]
    public bool ShowModuleAgreement = true;

    /// <summary>
    /// GitHub から入れたモジュールに新しい版があるかを、自分から確かめるか（起動してしばらくしてから、1 日に 1 回まで）。
    /// 確かめるだけで、入れるのは管理画面で押したとき。切っても、管理画面の「更新を確認」は使える。
    /// GitHub から入れたものが無ければ、どこにも繋がない。
    /// </summary>
    [SpoitableSettingsGroup("host.settings.group.modules")]
    [SpoitableSettingsLabelLocalize("host.settings.checkModuleUpdates.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.checkModuleUpdates.description")]
    public bool CheckModuleUpdates = true;

    /// <summary>
    /// ソースから build するモジュールがいくつもあるとき、並べて build するか。切ると 1 つずつ（残りは、前の build が終わるのを待つ）。
    /// 落とすのは、切っていても並行。
    /// </summary>
    [SpoitableSettingsGroup("host.settings.group.modules")]
    [SpoitableSettingsLabelLocalize("host.settings.parallelModuleBuilds.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.parallelModuleBuilds.description")]
    public bool ParallelModuleBuilds = true;

    /// <summary>利用者が選ぶまでは、入っている版のチャンネルに合わせる（<see cref="Updates.AppUpdates.FollowInstalledChannel"/>）。</summary>
    [SpoitableSettingsGroup("host.settings.group.updates")]
    [SpoitableSettingsLabelLocalize("host.settings.updateChannel.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.updateChannel.description")]
    public UpdateChannel UpdateChannel = UpdateChannel.Stable;

    [SpoitableSettingsGroup("host.settings.group.updates")]
    [SpoitableSettingsLabelLocalize("host.settings.checkAppUpdates.label")]
    [SpoitableSettingsDescriptionLocalize("host.settings.checkAppUpdates.description")]
    public bool CheckAppUpdates = true;

    [SpoitableSettingsHidden]
    public string SkippedAppVersion = "";

    [SpoitableSettingsHidden]
    public bool UpdateChannelChosen;

    /// <summary>
    /// 置いてあるテーマの名前を引く。<see cref="Theme"/> の候補に使う。
    /// internal なので設定の項目にはならない（reflection は public だけを見る）。
    /// </summary>
    internal Func<IReadOnlyList<string>>? ThemeNames;

    /// <summary>
    /// 状態を出しているものを引く。<see cref="HiddenSidebarStatus"/> / <see cref="HiddenWristItems"/> の候補に使う。
    /// internal なので設定の項目にはならない（reflection は public だけを見る）。
    /// </summary>
    internal Func<IReadOnlyList<string>>? StatusItems;

    /// <summary>
    /// 手首に置ける画面を引く。<see cref="WristShortcuts"/> の候補に使う。
    /// internal なので設定の項目にはならない（reflection は public だけを見る）。
    /// </summary>
    internal Func<IReadOnlyList<string>>? ShortcutTargets;

    public IReadOnlyList<string>? SuggestValuesFor(string memberName) => memberName switch
    {
        nameof(Theme) => ThemeNames?.Invoke(),
        nameof(HiddenSidebarStatus) or nameof(HiddenWristItems) => StatusItems?.Invoke(),
        nameof(WristShortcuts) => ShortcutTargets?.Invoke(),
        nameof(HiddenWristNotes) => WristNoteKinds,
        _ => null,
    };

    /// <summary>
    /// ログ画面で畳んでいる種別。設定画面には出さない — 読みながらチップで切り替えるものなので、画面はログ側が持ち、ここは置き場だけ。
    /// 既定で <c>ApiRequest</c> を畳むのは、1 セッションで 1000 件を超えて join/leave や動画の失敗（1 桁件数）を押し流すため。データは捨てず表示だけ畳む。
    /// <c>raw</c>（構造化できなかった生行）も既定で畳む。全件ログは 1 セッションで 15,000 行を超え、出したままだとイベントが 1 画面も保たずに流れる。
    /// </summary>
    [SpoitableSettingsHidden]
    public string[] HiddenKinds = [nameof(ApiRequest), "raw"];

    /// <summary>
    /// 設定画面で畳んであるカテゴリ。<c>"モジュール:入れ子の宛先"</c>（例 <c>"hrs:Parameters"</c>）。
    /// 畳みは値ではない（中の設定は畳んでも効いていて、見えないだけ）。覚えるのは、普段触らない項目を一度落としたら次に開いたときも落ちていてほしいから。既定は開。
    /// 画面はここ（設定画面の見出し）で、設定の項目としては出さない（<see cref="HiddenKinds"/> と同じく置き場としてだけ使う）。
    /// 知らない名前が残っていても消さない（モジュールを一時的に外しただけかもしれず、掃除すると戻したときに畳みが失われる）。
    /// </summary>
    [SpoitableSettingsHidden]
    public string[] CollapsedCategories = [];

    /// <summary>
    /// 外部ツールを最後に確認した時刻（ISO 8601）。利用者が触るものではないので画面に出さない。
    /// 消えても困らない（次の起動で 1 回余分に GitHub を見るだけ）。
    /// </summary>
    [SpoitableSettingsHidden]
    public Dictionary<string, string> ToolVerifiedAt = [];

    /// <summary>
    /// 手首の板を掴んで動かした置き場。キーは <c>left</c> / <c>right</c>、値は
    /// コントローラーから見た板の 3×4 行列（OpenVR と同じ並びの 12 個）。
    /// <para>
    /// 手ごとに覚える。 左で合わせた位置を右へ持ち越すと、鏡写しにならずに変な所へ出る。
    /// 無い手は既定の置き場。画面には出さない — 動かすのは VR の中で掴んで、戻すのは手首の ↺。
    /// </para>
    /// </summary>
    [SpoitableSettingsHidden]
    public Dictionary<string, float[]> WristOffsets = [];

    /// <summary>
    /// 手首の板の横幅（メートル）。null は既定（変えたことが無い）。
    /// <para>
    /// 両手で 1 つ。 置き場を手ごとに覚えるのは鏡写しにならないからで、幅にはその問題が無い。
    /// 画面には出さない — 変えるのは VR の中の Layout Mode で、戻すのは手首の ↺。
    /// 範囲の外の値（手で書き換えたもの）は手首の側で丸める。
    /// </para>
    /// </summary>
    [SpoitableSettingsHidden]
    public float? WristWidth;

    /// <summary>
    /// 空間の窓の横幅（メートル）。null は既定（変えたことが無い）。
    /// 画面には出さない — 変えるのは VR の中（掴んだままトリガーを引いて前後に動かす）。
    /// 範囲の外の値（手で書き換えたもの）は窓の側で丸める。
    /// </summary>
    [SpoitableSettingsHidden]
    public float? VrWindowWidth;

    /// <summary>
    /// 手首に付ける窓の横幅（メートル）。空間に置くときとは別（手首の上は顔に近い）。null は既定。画面には出さない。
    /// </summary>
    [SpoitableSettingsHidden]
    public float? VrWindowWristWidth;

    /// <summary>
    /// 手首に付ける窓を掴んで動かした置き場。キーは <c>left</c> / <c>right</c>、値は手首の板から見た窓の 3×4 行列
    /// （手首の板を動かすと窓も付いてくる）。無い手は板の真上。画面には出さない（動かすのは VR の中で掴んで）。
    /// </summary>
    [SpoitableSettingsHidden]
    public Dictionary<string, float[]> VrWindowWristOffsets = [];
}
