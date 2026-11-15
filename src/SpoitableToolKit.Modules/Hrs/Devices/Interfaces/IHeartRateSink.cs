namespace SpoitableToolKit.Modules.Hrs.Devices.Interfaces;

/// <summary>
/// 入口（<see cref="IHeartRateSource"/>）が知らせる口。受けるのは <see cref="SourceAsDevice"/> で、窓口へは
/// <see cref="IHeartRateDeviceSink"/> で渡す。どちらも入口のスレッドから呼ぶ。
/// </summary>
internal interface IHeartRateSink
{
    /// <summary>1 拍届いた。</summary>
    void Beat(IHeartRateSource from, int bpm);

    /// <summary>繋がり（<see cref="IHeartRateSource.Connected"/>・<see cref="IHeartRateSource.Connection"/>）が変わったかもしれない。同じままでも呼んでよい。</summary>
    void LinkChanged(IHeartRateSource from);
}
