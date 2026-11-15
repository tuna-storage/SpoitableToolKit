namespace SpoitableToolKit.Abstractions.Tools;

/// <summary>ホストが面倒を見る外部バイナリ 1 つ。作るのはホストだけ（項目はあとから足せるように、位置引数ではなく init で持つ）。</summary>
public sealed record ExternalTool
{
    /// <summary><see cref="ExternalTools"/> の識別子。</summary>
    public required string Id { get; init; }

    /// <summary>実行ファイルの絶対パス。</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>
    /// そのツールに使わせるキャッシュ置き場。<b>必ず渡すこと</b>
    /// （yt-dlp なら <c>--cache-dir</c>、deno なら環境変数 <c>DENO_DIR</c>）。
    /// 渡さないとユーザープロファイル直下に勝手に作られる。
    /// </summary>
    public required string CacheDirectory { get; init; }

    /// <summary>
    /// ホストが持ってきたコピーかどうか。<c>false</c> は PATH 上で見つけたもので、
    /// 他人が入れた物なのでホストは更新しない。
    /// </summary>
    public required bool IsManagedByHost { get; init; }
}
