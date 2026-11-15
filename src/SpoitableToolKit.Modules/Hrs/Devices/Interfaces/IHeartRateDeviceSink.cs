namespace SpoitableToolKit.Modules.Hrs.Devices.Interfaces;

/// <summary>
/// 見つける係（<see cref="IHeartRateDeviceDiscovery"/>）が窓口へ知らせる口。実装するのは窓口だけ。どれも係のスレッドから呼ぶ。
/// <para>
/// 窓口が知っている心拍計は、ここへ 1 度でも来たものだけ（<see cref="Found"/> を飛ばして <see cref="LinkChanged"/> や <see cref="Beat"/> が来ても、そこで覚える）。
/// 並びは来た順 — 持ち主が居ないうちに、同時に繋がっていたものの中から選ぶのはこの順。
/// </para>
/// </summary>
internal interface IHeartRateDeviceSink
{
    /// <summary>見つけた（まだ繋がっていなくてよい）。同じ心拍計で何度呼んでもよい。</summary>
    void Found(IHeartRateDevice device);

    /// <summary>繋がり（<see cref="IHeartRateDevice.Connection"/>）が変わったかもしれない。同じままでも呼んでよい。</summary>
    void LinkChanged(IHeartRateDevice device);

    /// <summary>1 拍届いた。0 は「計れていない」（肌から離れた）。</summary>
    void Beat(IHeartRateDevice device, int bpm);
}
