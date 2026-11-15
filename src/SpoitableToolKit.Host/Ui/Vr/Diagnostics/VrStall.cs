namespace SpoitableToolKit.Host.Ui.Vr.Diagnostics;

/// <param name="Stuck"><c>true</c> は「止まっている」、<c>false</c> は「また回り出した」。</param>
/// <param name="Where">止まったときに何の中だったか。回り出した報せでは、回り出したあとの場所。</param>
internal readonly record struct VrStall(bool Stuck, TimeSpan For, string Where);
