using SpoitableToolKit.Host.Ui.Push;

namespace SpoitableToolKit.Tests.Helpers;

/// <summary>流れたものを溜めるだけの差し替え。</summary>
internal class Recording : UiBroadcast
{
    public List<(string Channel, object Payload)> Sent { get; } = [];

    public override void Push(string channel, object payload)
    {
        lock (Sent) Sent.Add((channel, payload));
    }

    /// <summary>匿名型のまま来るので、名前で引く。</summary>
    public static object? Field(object payload, string name) =>
        payload.GetType().GetProperty(name)?.GetValue(payload);

    public (string Channel, object Payload)[] On(string channel)
    {
        lock (Sent) return [.. Sent.Where(s => s.Channel == channel)];
    }
}
