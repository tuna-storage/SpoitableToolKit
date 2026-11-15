namespace SpoitableToolKit.Abstractions.Tools;

/// <summary>
/// 外部バイナリの取得と鮮度管理。同梱せず実行時に取ってくる。
/// <para>
/// yt-dlp は数週間古いだけで YouTube に弾かれるので、ホストが持っているコピーは
/// SHA-256 を見て入れ替える（PATH 上で見つけたものは他人の持ち物なので触らない）。
/// </para>
/// </summary>
public interface IExternalToolProvider
{
    /// <summary>
    /// 実行ファイルを用意して返す。取れなければ null（ネットワークが無い、配布が変わった等）。
    /// 呼び出しは直列化されるので、複数のモジュールが同時に呼んでも二重に落としてこない。
    /// </summary>
    Task<ExternalTool?> GetAsync(string toolId, CancellationToken cancellationToken = default);
}
