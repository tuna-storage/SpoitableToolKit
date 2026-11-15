namespace SpoitableToolKit.Modules.Hrs.Avatar;

/// <summary>
/// アバターへ送る 1 回分の元。<b>どのプリセットにも同じものを渡す</b>（何をどの名前で送るかはプリセットが決める）。
/// </summary>
/// <param name="Bpm">
/// 送る心拍。設定の「値をなめらかに動かす」が入なら、届いた値へ寄せている途中の値（小数）。
/// 0 は測れていない・切れた・黙った・止めた（どれでも同じに扱う）。
/// </param>
/// <param name="Average">直近 <see cref="HeartRateAverage.Window"/> に届いた心拍の平均（寄せ方は <paramref name="Bpm"/> と同じ）。止まっていれば 0。</param>
/// <param name="Beat">拍の始まりから少しの間だけ true。拍を作るのは session で、どのプリセットにも同じ拍が届く。</param>
/// <param name="Beats">
/// 心拍が届き始めてから数えた拍の数（1 拍目で 1）。<b>止まると 0 に戻る</b>。
/// 「拍ごとに入れ替える」形のプリセットは、これが奇数か偶数かで決める — プリセットに自分で数えさせると、状態を持つことになる。
/// </param>
internal readonly record struct HeartRateInfo(float Bpm, float Average, bool Beat, long Beats)
{
    /// <summary>心拍が届いているか。<b>0 なら届いていない</b>（繋がっていても、アバターには繋がっていないと見せる）。</summary>
    public bool Live => Bpm > 0;

    /// <summary>
    /// 整数で送る心拍（四捨五入）。<b>桁に分ける値もこれから取る</b> — 桁ごとに丸めると、79.6 が「7 と 0」になったりする。
    /// </summary>
    public int WholeBpm => (int)MathF.Round(Bpm, MidpointRounding.AwayFromZero);

    public int WholeAverage => (int)MathF.Round(Average, MidpointRounding.AwayFromZero);

    public static HeartRateInfo Stopped => new(0, 0, false, 0);
}
