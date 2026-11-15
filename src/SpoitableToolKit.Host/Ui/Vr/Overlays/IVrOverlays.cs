using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Pointing;

namespace SpoitableToolKit.Host.Ui.Vr.Overlays;

/// <summary>OpenVR そのもの。ここから先は試験に出せない。実物は <see cref="OpenVrOverlays"/> の 1 枚だけで、判断は何も持たない（「いつ作るか」「いつ諦めるか」は <see cref="VrHost"/> が決める）。</summary>
internal interface IVrOverlays
{
    /// <summary>SteamVR が起動しているか。これを見てから <see cref="Start"/> する — 居ないのに繋ぎに行くと SteamVR が起動する。閉じる途中の SteamVR は「居ない」と答える（<see cref="OpenVrOverlays.IsAvailable"/> の注記）。</summary>
    bool IsAvailable();

    /// <summary>繋ぐ。失敗の理由は <paramref name="error"/> に入る（画面には出さない）。</summary>
    bool Start(out string? error);

    void Stop();

    /// <summary>ダッシュボードのタブ。返るのは板の側の handle。</summary>
    ulong CreateDashboard(string key, string name, float widthInMeters);

    /// <summary>絵を差し替える。渡すのは GPU のテクスチャ。CPU バッファで上げる道（<c>SetOverlayRaw</c>）は、1 回上げるたびに画面が乱れ、回数を増やすほど悪化したので使わない。</summary>
    void UploadTexture(ulong handle, nint texture);

    /// <summary>SteamVR が「閉じるので付き合え」と言ってきたか。返事をしないと「アプリを待っています」で止まる。</summary>
    bool QuitRequested();

    /// <summary>レーザーで触れるようにする。板の画素数を渡すので、座標が画素で返るようになる。スクロールは旗を立てないと飛んで来ない。</summary>
    void EnableInput(ulong handle, int width, int height);

    /// <summary>溜まっているレーザーの出来事。座標は OpenVR の流儀のまま返す（画面の座標に直すのは <see cref="VrPointerMap"/> の仕事）。</summary>
    IReadOnlyList<VrPointer> PollPointer(ulong handle);

    void Destroy(ulong handle);

    /// <summary>ダッシュボードの外に出す板（手首用）。返るのは handle、作れなければ 0。作っただけでは見えない（<see cref="SetShown"/> で出す）。ダッシュボードの板とは逆。</summary>
    ulong CreateOverlay(string key, string name, float widthInMeters);

    /// <summary><paramref name="offset"/> は機器から見た板の位置と向き。</summary>
    void Attach(ulong handle, uint device, VrMatrix offset);

    /// <summary>板を空間に置く（立った姿勢の原点から見た位置と向き）。機器には付けない — 空間の窓は手を動かしても付いてこない。</summary>
    void PlaceAbsolute(ulong handle, VrMatrix pose);

    /// <summary>出す / 隠す。ダッシュボードの板には使わない（SteamVR が出し入れする）。</summary>
    void SetShown(ulong handle, bool shown);

    /// <summary>板の透明度（0 で透明、1 で不透明。OpenVR の既定は 1）。出入りのフェードに使う。</summary>
    void SetAlpha(ulong handle, float alpha);

    /// <summary>板の中心を保ったまま大きくなる。</summary>
    void SetWidth(ulong handle, float widthInMeters);

    /// <summary>繋がっていなければ null（null ドライバでは常に null）。</summary>
    uint? ControllerFor(VrHand hand);

    /// <summary>全機器の今の姿勢（立った姿勢の原点から見た位置と向き）。番号が添字で、追えていない機器は null。頭は 0 番。</summary>
    IReadOnlyList<VrMatrix?> Poses();

    /// <summary>ヘッドセットのリフレッシュレート（Hz）。周の速さをこれに合わせる（<see cref="VrHost.FrameRateFor"/>）。読めなければ null。</summary>
    float? DisplayFrequency();

    /// <summary>開いている間は SteamVR のレーザーが出るので、自前のポインタは引っ込める。</summary>
    bool DashboardOpen();

    /// <summary>
    /// 両手のボタンと狙いの姿勢。1 周に 1 回だけ呼ぶ（中で <c>UpdateActionState</c> を呼ぶ）。
    /// SteamVR のレーザーの旗（<c>MakeOverlaysInteractiveIfVisible</c>）は使わない: 旗が立っている間は SteamVR が入力を全部取り、手首が見えているだけで VRChat の操作が全部止まる。だから自前で読む。読んでも VRChat からは取り上げない。
    /// 読む用意ができていなければ（action manifest を断られた等）、両手とも何も押していない・狙いも無い。
    /// <paramref name="captureAll"/> が true の周は入力を全部こちらで預かる（Layout Mode の間。VRChat に届かない）。優先度の高い action set（<c>/actions/layout</c> と <c>/actions/pointer</c>）で、割り当てた入力元を下のアプリから外す。SteamVR に断られたら預からずに読む（板は動かせる。入力が VRChat にも届くだけ）。
    /// </summary>
    VrHands ReadHands(bool captureAll);

    void Pulse(VrHand hand, float seconds, float amplitude);

    /// <summary>自前のポインタの絵の板（当たった所の点）。絵は作るときに 1 度だけ渡す（<c>SetOverlayRaw</c>。毎周上げる道が駄目なのは CEF の大きな絵の話）。板より手前に描く（並びの順を上げてある）。作れなければ 0。</summary>
    ulong CreateSprite(string key, string name, VrSprite sprite, float widthInMeters);

    /// <summary>SteamVR の側で計算した、狙いと板の当たり（UV）。確かめるためだけに使う（自前の計算と並べてログに出す）。当たらなければ null。</summary>
    (float U, float V)? Intersect(ulong handle, VrMatrix aim);
}
