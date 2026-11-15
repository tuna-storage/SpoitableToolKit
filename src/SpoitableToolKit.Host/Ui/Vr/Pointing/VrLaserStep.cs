namespace SpoitableToolKit.Host.Ui.Vr.Pointing;

/// <param name="Target">いま指している板の handle。指していなければ 0。</param>
/// <param name="GrabPressed">板を指したまま掴む（グリップ）を押した。掴むかどうかは板の持ち主が決める。</param>
/// <param name="Pressed">板を指したままトリガーを引いた（この周に）。反対の手の流れ（<see cref="VrLaser.GlidingOn"/>）を止めるのに使う。</param>
internal readonly record struct VrLaserStep(ulong Target, bool GrabPressed, bool Pressed = false);
