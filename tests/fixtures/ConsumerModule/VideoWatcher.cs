using SpoitableToolKit.Abstractions.Events.Video;

namespace ConsumerModule;

/// <summary>
/// listener を本体から切り離した見本。<b>ここが「クラス分けできる」ことの実物</b>で、
/// 持ち主を明示で渡すからこそ、別クラスでもホストが「誰の listener か」を知っていられる
/// （畳むときはホストがまとめて外す）。
/// </summary>
internal sealed class VideoWatcher(string dataDirectory) : IVideoEventListener
{
    public void OnVideoPlaybackFailed(VideoPlaybackFailedEventParams e) =>
        File.WriteAllText(Path.Combine(dataDirectory, "video-failed.txt"), e.Video.Url);
}
