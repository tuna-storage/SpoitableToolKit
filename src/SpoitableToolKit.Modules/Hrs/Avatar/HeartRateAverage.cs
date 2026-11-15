namespace SpoitableToolKit.Modules.Hrs.Avatar;

/// <summary>
/// 直近 <see cref="Window"/> に届いた心拍の平均。<b>届いた回数で割る</b>（拍の数ではない）。
/// <para>
/// 足すのは BLE のスレッド（<see cref="HeartRateSession.Report"/>）、読むのは送るループなので、<b>鍵を掛けて持つ</b>。
/// 0（測れていない）は足さない。
/// </para>
/// </summary>
internal sealed class HeartRateAverage
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly Lock _lock = new();
    private readonly Queue<(DateTimeOffset At, int Bpm)> _samples = new();

    public void Add(DateTimeOffset at, int bpm)
    {
        if (bpm <= 0) return;

        lock (_lock) _samples.Enqueue((at, bpm));
    }

    /// <summary>止まったら捨てる。戻ったあとの平均に、止まる前の値を混ぜない。</summary>
    public void Clear()
    {
        lock (_lock) _samples.Clear();
    }

    /// <summary>
    /// <paramref name="now"/> から見た平均（四捨五入）。窓の中に 1 つも無ければ <c>null</c>
    /// — 心拍計が黙ったままでも、止まったとみなす秒数（最大 120 秒）までは窓より長く待つので、ここに来る。
    /// </summary>
    public int? Of(DateTimeOffset now)
    {
        lock (_lock)
        {
            while (_samples.TryPeek(out var oldest) && oldest.At <= now - Window)
                _samples.Dequeue();

            if (_samples.Count == 0) return null;

            var sum = 0L;
            foreach (var (_, bpm) in _samples) sum += bpm;
            return (int)Math.Round(sum / (double)_samples.Count, MidpointRounding.AwayFromZero);
        }
    }
}
