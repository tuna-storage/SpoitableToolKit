namespace SpoitableToolKit.Host.Config.Interfaces;

/// <summary>
/// 動きの行が変わった（モジュールの <c>Changed()</c>・結んだ・ボタンの分の走り始めと終わり）を受けて、画面へ流す係。
/// <b>呼ばれるのはスレッドプールの上</b>（ストアが呼んだ側の手元から外してから呼ぶ。モジュールが自分の lock を持ったまま <c>Changed()</c> を呼んでも、その手元で describe を走らせない）。
/// </summary>
internal interface IConfigActionSink
{
    /// <param name="key">行の宛先（<see cref="ConfigMember.Path"/>。画面の <c>data-key</c>）。</param>
    void Changed(string moduleId, string key);
}
