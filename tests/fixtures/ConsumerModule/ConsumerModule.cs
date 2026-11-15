using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Events.Video;
using SpoitableToolKit.Abstractions.Shortcuts;
using SpoitableToolKit.Abstractions.Ui;
using TestContract;

namespace ConsumerModule;

/// <summary>
/// 契約を使う側の見本。
/// <para>
/// 結果はデータディレクトリへのファイル出力で観測する。テスト側からは
/// モジュールの中は見えない（別 ALC の別アセンブリなので）ため、効果だけを見る。
/// </para>
/// <para>
/// 画面まわりは OnInit で <c>host.Ui</c> に登録する。<b>呼ばれる口と右クリックは別のクラス</b>（<see cref="Calls"/>・<see cref="Menu"/>）—
/// モジュールのクラスに interface を実装しなくても渡せることを、ここで見る。
/// </para>
/// </summary>
public sealed class ConsumerModule : ISpoitableModule
{
    private IModuleHost? _host;

    /// <summary>サイドバーの札。中身は試験が見る（ホストと共有する）。</summary>
    private readonly ModuleStatus _status = new() { Text = "consumer status" };

    public string Id => "consumer";

    public string DisplayName => "Consumer";

    public bool OnInit(IModuleHost host)
    {
        _host = host;

        // web/ は同梱していない（試験が置くかどうかを決める）。置かなければ、ホストは「パネル無し」に倒すはず。
        host.Ui.RegisterPanel("index.html");
        host.Ui.RegisterVrPanel("vr.html");
        host.Ui.RegisterEndpoint(new Calls());
        host.Ui.RegisterContextMenu(new Menu());
        host.Ui.RegisterStatus(_status);
        host.Shortcuts.Register(new Tile("ping"));

        // 本体ではなく別クラスを登録する。外すのはホストの仕事なので OnShutdown は要らない。
        host.Videos.AddListener<IVideoEventListener>(this, new VideoWatcher(host.DataDirectory));
        return true;
    }

    public void OnAllModulesLoaded()
    {
        var greeter = _host!.ExternalModules.GetRequired<IGreeter>(IGreeter.ModuleIdentity);
        File.WriteAllText(Path.Combine(_host.DataDirectory, "greeting.txt"),
            $"{greeter.Instance!.Greet()} via {typeof(IGreeter).Assembly.GetName().Name}");

        // OnInit の後の登録は断られるはず。断られた文を残す（試験が読む）。
        try
        {
            _host.Ui.RegisterStatus(new ModuleStatus());
            File.WriteAllText(Path.Combine(_host.DataDirectory, "late-register.txt"), "accepted");
        }
        catch (InvalidOperationException ex)
        {
            File.WriteAllText(Path.Combine(_host.DataDirectory, "late-register.txt"), ex.Message);
        }

        // 手首のショートカットも同じ（host.Ui と同じ窓）。
        try
        {
            _host.Shortcuts.Register(new Tile("late"));
            File.WriteAllText(Path.Combine(_host.DataDirectory, "late-shortcut.txt"), "accepted");
        }
        catch (InvalidOperationException ex)
        {
            File.WriteAllText(Path.Combine(_host.DataDirectory, "late-shortcut.txt"), ex.Message);
        }
    }

    /// <summary>手首のショートカット（押しても何もしない）。</summary>
    private sealed class Tile(string id) : IClickableWristShortcut
    {
        public string Id => id;

        public string Name => "consumer.shortcut." + id;

        public Task OnTriggerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// 畳まれるときに一度ホストへ戻る。<b>ここが「listener をいつ外すか」の観測点。</b>
    /// <para>
    /// ホスト側（試験）はこの中でイベントを配る。正常系では OnShutdown の<b>あと</b>に外すので
    /// <see cref="VideoWatcher"/> に届き、失敗時は<b>先に</b>外すので届かない。
    /// 棚を外から読む口は無いので、順番はこうやって「届くかどうか」でしか観測できない。
    /// </para>
    /// <para>
    /// 合図を <c>shared/</c> の契約ではなく <see cref="Action{T}"/> で受けるのは、
    /// <b>BCL の型は ALC をまたいでも同一</b>だから。試験用の契約を shared/ に置くと
    /// 「ホスト本体にも入っている」経路（<c>SharedAssemblies</c>）へ倒れて、
    /// どちらのコピーが使われるかが試験の実行順で変わる。
    /// </para>
    /// <para>合図が無い（<c>Services</c> が何も返さない）ときは何もしない。</para>
    /// </summary>
    public void OnShutdown() =>
        (_host?.Services.GetService(typeof(Action<string>)) as Action<string>)?.Invoke(Id);

    /// <summary>ページから呼ばれる口（モジュールのクラスとは別）。受けた名前と中身をそのまま返す。</summary>
    private sealed class Calls : IUiEndpoint
    {
        public Task<string> HandleUiCallAsync(string method, string payload, CancellationToken cancellationToken = default) =>
            Task.FromResult($"{{\"method\":\"{method}\",\"payload\":{(payload.Length == 0 ? "null" : payload)}}}");
    }

    /// <summary>右クリックの項目（モジュールのクラスとは別）。</summary>
    private sealed class Menu : IContextMenuSource
    {
        public IReadOnlyList<ContextMenuItem> GetContextMenu(ContextMenuTarget target) =>
            [new ContextMenuItem { Id = "consumer-item", LabelKey = "consumer.menu.item" }];

        public Task InvokeContextMenuAsync(string id, ContextMenuTarget target, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
