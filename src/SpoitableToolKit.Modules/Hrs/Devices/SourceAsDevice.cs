using SpoitableToolKit.Modules.Hrs.Devices.Interfaces;
using SpoitableToolKit.Modules.Hrs.Devices.Models;

namespace SpoitableToolKit.Modules.Hrs.Devices;

/// <summary>
/// 入口（<see cref="IHeartRateSource"/> — BLE・HDS）を、1 台の心拍計とそれを見つける係として窓口に見せる。
/// <para>
/// 入口は繋ぐ相手を途中で替えることがある（BLE は見えている別の心拍計へ・HDS は別の送り主へ）ので、<see cref="Identity"/> も替わる —
/// 窓口は「同じ心拍計か」を <see cref="MonitorIdentity.IsSame"/> で見るので、これで足りる。
/// </para>
/// </summary>
internal sealed class SourceAsDevice : IHeartRateDevice, IHeartRateDeviceDiscovery, IHeartRateSink, IDisposable
{
    private readonly IHeartRateSource _source;
    private volatile IHeartRateDeviceSink? _sink;
    private readonly Lock _gate = new();

    /// <summary>最後に繋がっていた相手（切れたあとの <see cref="Identity"/>）。</summary>
    private MonitorIdentity _last;

    /// <summary>この 1 台として手で切った相手（<see cref="Connect"/> で全部を戻す）。</summary>
    private readonly List<MonitorIdentity> _heldOff = [];

    public SourceAsDevice(IHeartRateSource source)
    {
        _source = source;
        source.ReportTo(this);
    }

    /// <summary>入口がスキャンで見つけているもの（設定の候補）。</summary>
    public IReadOnlyList<MonitorIdentity> Discovered => _source.Discovered;

    public MonitorIdentity Identity
    {
        get
        {
            if (_source.Connected is not { } connected)
                lock (_gate) return _last;

            lock (_gate) _last = connected;
            return connected;
        }
    }

    public HeartRateConnection Connection => _source.Connected is null ? HeartRateConnection.None : _source.Connection;

    public int? Battery => _source.Battery;

    public TimeSpan SilenceAfter => _source.SilenceAfter;

    public void Disconnect()
    {
        var monitor = Identity;

        lock (_gate) _heldOff.Add(monitor);

        _source.Disconnect(monitor);
    }

    public void Connect()
    {
        MonitorIdentity[] held;

        lock (_gate)
        {
            held = [.. _heldOff];
            _heldOff.Clear();
        }

        foreach (var monitor in held) _source.Connect(monitor);
    }

    public Task DiscoverAsync(DeviceSelector selector, IHeartRateDeviceSink sink, CancellationToken cancellationToken)
    {
        _sink = sink;
        sink.Found(this);
        return _source.RunAsync(selector, cancellationToken);
    }

    void IHeartRateSink.Beat(IHeartRateSource from, int bpm) => _sink?.Beat(this, bpm);

    void IHeartRateSink.LinkChanged(IHeartRateSource from) => _sink?.LinkChanged(this);

    public void Dispose() => (_source as IDisposable)?.Dispose();
}
