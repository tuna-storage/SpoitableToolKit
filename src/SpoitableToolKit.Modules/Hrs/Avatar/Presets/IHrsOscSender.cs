using SpoitableToolKit.Abstractions.Osc;

namespace SpoitableToolKit.Modules.Hrs.Avatar.Presets;

/// <summary>
/// アバターの仕組み 1 つぶんの送り方（プリセット 1 つ）。<b>同じ <see cref="HeartRateInfo"/> から、仕組みごとに違う名前と値を送る。</b>
/// <para>
/// <b>タイマーも状態も持たない。</b> いつ送るか・拍をいつ立てるかは session が決め、ここは届いた 1 回分をそのまま送る。
/// 値で比べられる record にしてあるので、session は「名前が変わった」「プリセットが変わった」を 1 回の比較で見つけられる。
/// </para>
/// </summary>
internal interface IHrsOscSender
{
    /// <summary>送る宛先の全部（<c>/avatar/parameters/</c> 付き）。替えるときに、新しい組にも残る宛先を見分けるのに使う。</summary>
    IReadOnlyCollection<string> Addresses { get; }

    /// <summary>
    /// 1 回分を送る。<b>毎回すべての名前へ送る</b> — UDP は届いた保証が無いので、変わったものだけ送ると取りこぼしたまま固まる。
    /// </summary>
    void Send(IOscBus osc, HeartRateInfo info);
}
