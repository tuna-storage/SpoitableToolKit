namespace SpoitableToolKit.Abstractions.Tools;

/// <summary>ホストが取得できる外部バイナリの識別子。</summary>
public static class ExternalTools
{
    /// <summary>
    /// yt-dlp。
    /// <para>
    /// <b>YouTube を相手にするなら <see cref="Deno"/> も一緒に要る。</b>
    /// nsig チャレンジの解答スクリプトは yt-dlp が持っているが、動かす JS エンジンは持っていない。
    /// エンジンが無いと <b>URL の抽出は成功したまま、その URL が HTTP 403 で弾かれる</b>。
    /// ツール側は成功に見えるので、ユーザーからは原因が分からない。
    /// 呼ぶときは <c>--js-runtimes deno:&lt;path&gt;</c> と <c>DENO_DIR</c> を必ず付けること。
    /// </para>
    /// </summary>
    public const string YtDlp = "yt-dlp";

    /// <summary>deno（yt-dlp の JS ランタイム）。</summary>
    public const string Deno = "deno";
}
