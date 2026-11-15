namespace SpoitableToolKit.Host.Modules.Management;

/// <summary>
/// 確かめた結果。<paramref name="Found"/> は今回<b>新しく</b>見つかった更新（<c>owner/name タグ</c>・<c>owner/name commit 7 桁</c>。
/// 前から分かっていたものは入らない — 同じ知らせを繰り返さない）。
/// <paramref name="Asked"/> は聞こうとした入れ元の数、<paramref name="Reached"/> は答えが返った数。
/// </summary>
internal sealed record ModuleUpdateCheck(IReadOnlyList<string> Found, int Asked, int Reached)
{
    /// <summary>聞こうとした入れ元の全部から答えが返ったか（1 つも聞かなかったときも true）。</summary>
    public bool Complete => Reached == Asked;
}
