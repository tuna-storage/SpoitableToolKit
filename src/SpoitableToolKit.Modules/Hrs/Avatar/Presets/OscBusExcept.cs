using SpoitableToolKit.Abstractions.Osc;

namespace SpoitableToolKit.Modules.Hrs.Avatar.Presets;

/// <summary>
/// <paramref name="skip"/> の宛先には送らない <see cref="IOscBus"/>。
/// <para>
/// 名前かプリセットを替えるとき、前の組の名前へ「止まった」を送るのに使う。<b>新しい組にも残る名前へは送らない</b> —
/// 送ると、すぐ後に本当の値で上書きするまでの間、アバターに一瞬 0 と false が見える（名前を 1 つ変えただけで、繋がっているが一瞬 false になる）。
/// </para>
/// </summary>
internal sealed class OscBusExcept(IOscBus inner, IReadOnlyCollection<string> skip) : IOscBus
{
    public void Send(string address, params object[] args)
    {
        if (!skip.Contains(address)) inner.Send(address, args);
    }
}
