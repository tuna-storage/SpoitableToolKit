using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Clipboard;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Abstractions.Events.VRChat;
using SpoitableToolKit.Abstractions.Events.Video;
using SpoitableToolKit.Abstractions.Events.VrcLog;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Abstractions.Modules;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Abstractions.Osc;
using SpoitableToolKit.Abstractions.Shortcuts;
using SpoitableToolKit.Abstractions.Tools;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Abstractions.VrcLog;

namespace SpoitableToolKit.Abstractions;

/// <summary>
/// モジュールからホストの横断機能へアクセスするための入口。
/// </summary>
public interface IModuleHost
{
    /// <summary>
    /// 本体のサービスの解決先。<see cref="IModuleHost"/> のプロパティに無いものはここから取る。
    /// </summary>
    IServiceProvider Services { get; }

    /// <summary>このモジュール専用のデータディレクトリ。呼ばれる時点で存在が保証される。</summary>
    string DataDirectory { get; }

    /// <summary>
    /// VRChat のログ（<see cref="IVrcLogEventListener"/>）。<c>host.VrcLog.AddListener&lt;IVrcLogEventListener&gt;(this, listener)</c>。
    /// いま読んでいるファイルは <see cref="IVrcLogBus.CurrentLogFile"/>。
    /// </summary>
    IVrcLogBus VrcLog { get; }

    /// <summary>ログの永続化。VRChat 自身のログは消えるので、残したいものはここへ。</summary>
    IVrcLogStore VrcLogStore { get; }

    /// <summary>
    /// 外部バイナリ（yt-dlp / deno）の取得。モジュールごとに落としてこない。
    /// </summary>
    IExternalToolProvider ExternalTools { get; }

    /// <summary>
    /// ユーザーへの通知。出し先（アプリ内 / VR オーバーレイ）はホストが決める。
    /// </summary>
    INotificationManager Notifications { get; }

    /// <summary>クリップボード。</summary>
    IClipboard Clipboard { get; }

    /// <summary>
    /// VRChat への OSC 送信。出し先（ポート）はホストが決める。
    /// </summary>
    IOscBus Osc { get; }

    /// <summary>
    /// 自分の設定（<see cref="ISpoitableModule.Config"/>）の変更（<see cref="IConfigEventListener{TConfig}"/>）。型は自分の設定の型そのもの:
    /// <c>host.Config.AddListener&lt;IConfigEventListener&lt;HrsConfig&gt;&gt;(this, listener)</c>。自分自身でも別クラスでもよい。
    /// 届くのは自分の設定の変更だけ（ほかのモジュールの設定は届かない）。設定を持たない（<see cref="ISpoitableModule.Config"/> が null の）モジュールが足すと断られる。
    /// 呼ばれるのは UI のスレッド。
    /// </summary>
    IEventSource Config { get; }

    /// <summary>
    /// 自分の出来事を配る口を作る所。ほかのモジュールに受けてもらう出来事を出すときに使う:
    /// <code>
    /// _hub = host.Events.Create&lt;ISomeListener&gt;(this);
    /// public IEventSource Listeners => _hub.Source;            // 受ける側に見せるのはこれだけ
    /// _hub.Fire&lt;ISomeListener&gt;(l => l.OnSomething(new SomethingEvent(...)));
    /// </code>
    /// owner には自分自身を渡す（ホストが照合するので、ほかのモジュールを名乗れない）。畳まれたら、作った口はホストが閉じる。
    /// </summary>
    IEventHubs Events { get; }

    /// <summary>
    /// VRChat が起動した・終わった（<see cref="IVRChatEventListener"/>）。<c>host.VRChat.AddListener&lt;IVRChatEventListener&gt;(this, listener)</c>。
    /// 呼ばれるのはホストの見張りのスレッド（UI のスレッドではない）。
    /// </summary>
    IEventSource VRChat { get; }

    /// <summary>
    /// 動画の再生が始まった・失敗した（<see cref="IVideoEventListener"/>）。<c>host.Videos.AddListener&lt;IVideoEventListener&gt;(this, listener)</c>。
    /// 呼ばれるのはログを読むスレッド（UI のスレッドではない）。生のログ行が要るときだけ <see cref="VrcLog"/> を使う。
    /// </summary>
    IEventSource Videos { get; }

    /// <summary>
    /// VRChat がいま動いているか。<b>状態を確かめる必要がある所だけで使う</b>（例: 押したその時に VRChat が居ないと意味の無い処理）。
    /// <para>
    /// <b>起動・終了に反応したいなら <see cref="IVRChatEventListener"/></b> を使う。周ごとに読んで変わりを探す口ではない。
    /// </para>
    /// <para>
    /// アプリの起動時に一度確かめてからモジュールを読むので、<see cref="ISpoitableModule.OnInit"/> の時点でもう正しい値が入っている。
    /// そのあとはホストが 2 秒おきに確かめるので、<b>最大 2 秒ほど古い</b>ことがある。
    /// </para>
    /// </summary>
    bool IsVRChatRunning { get; }

    /// <summary>
    /// 自分の画面まわり（<see cref="IModuleUi"/>）。<b>パネル・呼ばれる口・右クリックの項目・様子は、<see cref="ISpoitableModule.OnInit"/> の中でここへ登録する</b>
    /// （<c>host.Ui.RegisterPanel("index.html")</c>・<c>host.Ui.RegisterEndpoint(this)</c> など）。登録しないものは出ない。
    /// 自分のページへ知らせるのは <c>host.Ui.Push</c>（いつでも）。
    /// </summary>
    IModuleUi Ui { get; }

    /// <summary>
    /// 手首のショートカット（<see cref="IModuleShortcuts"/>）。<b><see cref="ISpoitableModule.OnInit"/> の中で登録する</b>
    /// （<c>host.Shortcuts.Register(new MyShortcut(...))</c>）。表示と、押されたときの処理を 1 つのクラス（<see cref="IWristShortcut"/>）で渡す。
    /// </summary>
    IModuleShortcuts Shortcuts { get; }

    /// <summary>
    /// 画面に出す文言。<b>ログには使わない</b>（ログは開発者のもの）。
    /// 辞書はモジュールが <c>locales/&lt;言語&gt;.json</c> で同梱する。
    /// </summary>
    ILocalizer Text { get; }

    /// <summary>
    /// このモジュールのログ（開発者が読むもの。画面の字は <see cref="Text"/>）。カテゴリは <c>SpoitableToolKit.Modules.&lt;Id&gt;</c> で、
    /// アプリのログの画面はこの頭のあとで、どのモジュールの行かを見分ける。<b>モジュールの中のクラスにも、これを渡して書く</b>（自分で logger を作らない）。
    /// <para>
    /// <see cref="ILogger"/> の dll（Microsoft.Extensions.Logging.Abstractions）は本体が読み込んだものを使う。SDK はコンパイルにだけ参照し、
    /// モジュールの出力には入れない（入れると型が本体と二重になり、ここで受け取れなくなる）。
    /// </para>
    /// </summary>
    ILogger Logger { get; }

    /// <summary>
    /// モジュール間でサービスをやり取りする場所。登録は <see cref="ISpoitableModule.OnPostInit"/>、
    /// 取得は <see cref="ISpoitableModule.OnAllModulesLoaded"/> 以降。
    /// </summary>
    IExternalModuleRegistry ExternalModules { get; }

    /// <summary>
    /// 自分の設定（<see cref="ISpoitableModule.Config"/>）を<b>コードから書き換えたあと</b>に呼ぶ。保存して、開いている設定画面にも知らせる。
    /// <para>
    /// 利用者が設定画面で変えたものは、ホストが保存するので呼ばなくてよい。
    /// 使いどころは「今後は聞かない」（<see cref="NotifyRequest.OptOut"/>）のように、<b>利用者が設定画面の外で選んだもの</b>を覚えるとき。
    /// 設定を持たないモジュールが呼んでも何も起きない。
    /// </para>
    /// </summary>
    void SaveConfig();
}
