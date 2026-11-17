using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Ui.Vr.Overlays;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Pointing;
using SpoitableToolKit.Host.Ui.Vr.Rendering;

namespace SpoitableToolKit.Host.Ui.Vr.Dashboard;

/// <summary>
/// SteamVR のダッシュボードのパネル（こちらのタブ）。作る・消す・1 周ぶん回す（SteamVR のレーザーを配ってから絵を上げる）だけで、いつ作るかは <see cref="VrHost"/> が決める。
/// 繋いでいる間に消して作り直すことがある（設定 <c>VrDashboard</c> を切って入れ直したとき）。作り直したパネルは何も持っていないので、同じ番号の絵でも上げ直す。
/// </summary>
internal sealed class DashboardPanel(IVrOverlays overlays, VrHost.TextureSource textures, VrHost.PointerSink pointers, ILogger logger)
{
    public const string Key = "dev.f2a.spoitable.dashboard";

    private const string Name = "SpoitableToolKit";

    private const float WidthInMeters = 2.0f;

    private readonly OverlayFeed _feed = new(overlays, textures);

    private ulong _handle;

    private int _lastX;

    private int _lastY;

    public bool Up => _handle != 0;

    public int Uploads => _feed.Uploads;

    /// <summary>作る。作れなければ false を返し、何も残さない。</summary>
    public bool Open()
    {
        if (_handle != 0) return true;

        _handle = overlays.CreateDashboard(Key, Name, WidthInMeters);

        if (_handle == 0)
        {
            logger.LogWarning("Could not create the dashboard overlay");
            return false;
        }

        // 板が出来てから触れるようにする。先に呼ぶと宛先が無い。
        overlays.EnableInput(_handle, VrSurface.Width, VrSurface.Height);

        logger.LogInformation("The VR dashboard overlay is up");
        return true;
    }

    /// <summary>消す。開いているダッシュボードでこちらのタブを見ている最中でもよい（SteamVR が別のタブへ移す）。</summary>
    public void Close()
    {
        if (_handle == 0) return;

        overlays.Destroy(_handle);

        _handle = 0;
        _feed.Forget();
    }

    /// <summary>1 周。無いときは何もしない（板の無い handle 0 へ読みにも渡しにも行かない）。</summary>
    public void Tick()
    {
        if (_handle == 0) return;

        Deliver();

        _feed.Hand(_handle);
    }

    /// <summary>絵より先に配る。押した結果が次の 1 枚に乗るので、逆にすると 1 枚ぶん遅れて見える。</summary>
    private void Deliver()
    {
        foreach (var pointer in overlays.PollPointer(_handle))
        {
            if (pointer.Kind == VrPointerKind.Scroll)
            {
                var (wheelX, wheelY) = VrPointerMap.ToWheel(pointer.ScrollX, pointer.ScrollY);

                // 巻いた場所は最後に指していたところ。スクロールの報せに座標は入っていない。
                pointers(VrPointerKind.Scroll, _lastX, _lastY, pointer.Button, wheelX, wheelY);
                continue;
            }

            (_lastX, _lastY) = VrPointerMap.ToBrowser(pointer.X, pointer.Y, VrSurface.Width, VrSurface.Height);
            pointers(pointer.Kind, _lastX, _lastY, pointer.Button, 0, 0);
        }
    }
}
