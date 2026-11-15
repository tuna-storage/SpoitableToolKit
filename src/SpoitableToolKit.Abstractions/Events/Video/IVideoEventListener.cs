namespace SpoitableToolKit.Abstractions.Events.Video;

/// <summary>
/// 動画の再生に関する通知。要るものだけ書けばよい。
/// <para>
/// <b>実装しただけでは呼ばれない。</b> <see cref="IModuleHost.Videos"/> に、型を書いて足すこと:
/// <code>
/// host.Videos.AddListener&lt;IVideoEventListener&gt;(this, this);
/// </code>
/// 外すのはホストの仕事（モジュールが畳まれたら、足した listener は全部外れる）。
/// </para>
/// <para>
/// 既定実装を持つインターフェースにしてあるのは、抽象クラスだと単一継承になり、
/// 動画もプレイヤーもワールドも聞きたいモジュールが詰むため。
/// </para>
/// </summary>
public interface IVideoEventListener : IEventListener
{
    /// <summary>
    /// 再生が始まった。
    /// <para>
    /// <b>AVPro バックエンドでしか発火しない。</b> Unity Video Player は成功しても VRChat の
    /// ログに何も書かないため、開始を観測する手段が無い（失敗が無音なのと同じ理由）。
    /// 「開始しなかったこと」を当てにする作りにはしないこと。
    /// </para>
    /// </summary>
    void OnVideoPlaybackStarted(VideoPlaybackStartedEventParams e) { }

    /// <summary>
    /// 再生に失敗した。
    /// <para>
    /// VRChat は失敗すると同じ URL で 4〜5 回やり直すので、1 本の動画で複数回呼ばれる。
    /// 通知やクリップボードのような「ユーザーに見せる」処理は
    /// <see cref="VideoPlaybackFailedEventParams.IsRepeatedFail"/> が false のときだけ行うこと。
    /// </para>
    /// </summary>
    void OnVideoPlaybackFailed(VideoPlaybackFailedEventParams e) { }
}
