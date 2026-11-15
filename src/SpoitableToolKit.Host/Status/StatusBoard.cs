using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Host.Ui.Shell;

namespace SpoitableToolKit.Host.Status;

/// <summary>
/// 状態の口（<see cref="ModuleStatus"/>）を集めて画面へ配る。
/// <para>
/// 出る場所はシェルが決める: モジュールの分はサイドバーのそのモジュールの行の右、ホストの分（<see cref="HostConfig.Id"/>）はタイトルバーの右。
/// 手首の帯とダッシュボードの札にも同じものが並ぶ。出したものは全部並び、利用者は設定で隠せる。順番はホストが先頭で、あとは読み込み順。
/// </para>
/// <para>
/// 書いた側からは何も知らされない。状態の口はどのスレッドからでも書かれる（心拍なら BLE のループ）ので、
/// <see cref="UiPump"/> の周期（200 ms ごと）に毎回組み立てて、前に配ったものと比べ、違うときだけ配る。
/// 組み立てるのは数件の record なので、毎回でも軽い。「サイドバーに出さない状態」を変えたときも、並ぶ中身が変わるので次の周で配られる。
/// </para>
/// </summary>
internal sealed class StatusBoard(HostConfig config, UiBroadcast broadcast)
{
    private readonly Lock _gate = new();

    /// <summary>並ぶ順そのもの。ホストを先に入れるので、あとは足された順に並ぶ。</summary>
    private readonly List<(string Id, ModuleStatus Status)> _items = [];

    /// <summary>最後に配ったもの。隠されている項目の変化をここで落とす。</summary>
    private StatusBoardItem[] _last = [];

    /// <summary>
    /// 手首へ最後に配ったもの。手首は別に絞る（設定「手首に出さないもの」）ので、
    /// 同じ中身でも、変わったかどうかは別々に見る。
    /// </summary>
    private StatusBoardItem[] _lastWrist = [];

    /// <summary>設定「サイドバーに出さない状態」「手首に出さないもの」の候補に出す名前。</summary>
    public IReadOnlyList<string> Names()
    {
        lock (_gate) return [.. _items.Select(i => i.Id)];
    }

    /// <summary>預かる。ホストを最初に、モジュールは読み込み順に呼ぶ。</summary>
    public void Register(string id, ModuleStatus status)
    {
        lock (_gate) _items.Add((id, status));
    }

    /// <summary>
    /// <see cref="UiPump"/> の周期から呼ばれる。毎回組み立てて、前に配ったものと違うときだけ配る
    /// （組んでいる最中に書き換わっても、次の周で違いとして拾うので、取りこぼさない）。
    /// </summary>
    public void Flush()
    {
        var current = Current();
        var wrist = Current(wrist: true);

        if (!current.SequenceEqual(_last))
        {
            _last = current;
            broadcast.Push("statusItems", current);
        }

        if (!wrist.SequenceEqual(_lastWrist))
        {
            _lastWrist = wrist;
            broadcast.Push("wristStatus", wrist);
        }
    }

    /// <summary>
    /// いま並ぶもの。UI の初期化（<c>getStatus</c>）からも読む。
    /// <paramref name="wrist"/> なら手首の分（「手首に出さないもの」で絞る）。
    /// </summary>
    public StatusBoardItem[] Current(bool wrist = false)
    {
        var hidden = Hidden(wrist);

        lock (_gate)
            return
            [
                .. _items
                    .Where(i => !hidden.Contains(i.Id, StringComparer.OrdinalIgnoreCase))

                    // 空の項目は並ばない（枠だけ増えるのが一番損）。
                    .Where(i => i.Status.Text.Length > 0)
                    .Select(i => new StatusBoardItem(i.Id, i.Status.Text, Wire(i.Status.State), i.Status.Note)),
            ];
    }

    /// <summary>
    /// CefSharp の bind 越しに返す形。record のままでは渡せない — record は <c>EqualityContract</c>（<c>System.Type</c>）を持つので、
    /// 橋を渡ると呼び出しが成功扱いにならない。push（<see cref="UiBroadcast"/>）は System.Text.Json なので record のままで通り、壊れるのは bind 越しだけ。
    /// </summary>
    public object[] ForBridge(bool wrist = false) =>
    [
        .. Current(wrist).Select(i => new { id = i.Id, text = i.Text, state = i.State, note = i.Note }),
    ];

    private string[] Hidden(bool wrist) => (wrist ? config.HiddenWristItems : config.HiddenSidebarStatus) ?? [];

    /// <summary>
    /// JSON には小文字で出す。enum をそのまま流すと数字になる
    /// （<c>UiBroadcast</c> は <c>JsonStringEnumConverter</c> を入れていない）ので、
    /// 画面の <c>data-state</c> に直接使える形にしてから渡す。
    /// </summary>
    internal static string Wire(StatusState state) => state switch
    {
        StatusState.Ok => "ok",
        StatusState.Warn => "warn",
        StatusState.Bad => "bad",
        _ => "off",
    };
}
