namespace SpoitableToolKit.Host.Ui.Shell;

/// <summary>
/// VR へ頼む口（<see cref="IVrControls"/>）を挿す所。挿すのは VR の周を立てたとき（<c>VrLoop.Start</c>）の 1 か所。
/// モジュールは VR より先に読み込まれるので、作るのは起動の頭。VR を使わない起動では空のまま。
/// </summary>
internal sealed class VrControlsSlot
{
    private volatile IVrControls? _current;

    public IVrControls? Current
    {
        get => _current;
        set => _current = value;
    }
}
