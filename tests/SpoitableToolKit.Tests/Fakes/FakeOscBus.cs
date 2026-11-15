using SpoitableToolKit.Abstractions.Osc;

namespace SpoitableToolKit.Tests.Fakes;

internal sealed class FakeOscBus : IOscBus
{
    public List<(string Address, object[] Args)> Sent { get; } = [];

    public void Send(string address, params object[] args)
    {
        lock (Sent) Sent.Add((address, args));
    }

    /// <summary>最後に送られた値。同じパラメータを何度も送る作りなので、末尾を見る。</summary>
    public object? Last(string address)
    {
        lock (Sent) return Sent.LastOrDefault(s => s.Address == address).Args?.FirstOrDefault();
    }
}
