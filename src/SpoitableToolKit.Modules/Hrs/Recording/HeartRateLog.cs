namespace SpoitableToolKit.Modules.Hrs.Recording;

/// <summary>
/// 直近の心拍。<b>正はこちら</b>で、パネルは開いたときに取りに来る。
/// <para>
/// パネルは開かれていないことのほうが多い（VRChat を見ている間はそもそも窓が無い）ので、
/// その間の push は誰も受け取らない。C# 側に残しておかないと、開いた瞬間のグラフが空になる。
/// </para>
/// <para>
/// 上限を切ってあるのは、常駐したまま何時間も動くため。1 秒に 1 つ来るので、
/// 180 件で直近 3 分ぶん。それより前を見たいのは記録の話で、この一覧の仕事ではない。
/// </para>
/// </summary>
internal sealed class HeartRateLog(int capacity = 180)
{
    private readonly Lock _gate = new();
    private readonly Queue<HeartRateSample> _samples = new();

    private long _sum;
    private long _count;
    private int _min;
    private int _max;

    /// <summary>切れた・心拍が来ない、の 0 を置いたあと、まだ次の 1 拍が来ていない（<see cref="Gap"/>）。</summary>
    private bool _inGap;

    public void Add(int bpm, DateTimeOffset at)
    {
        lock (_gate)
        {
            // 切れていた所から上がる: 線が 0 のまま次の拍まで伸びてから上がるように、もう 1 点 0 を置く。
            if (_inGap)
            {
                Enqueue(new HeartRateSample(0, at));
                _inGap = false;
            }

            Enqueue(new HeartRateSample(bpm, at));

            // 最小 / 最大 / 平均は**捨てた分も含めて**数える。3 分より前の最大値が
            // 消えるのは、配信のオーバーレイに出す値としては嘘に近い。
            _sum += bpm;
            _count++;
            _min = _count == 1 ? bpm : Math.Min(_min, bpm);
            _max = Math.Max(_max, bpm);
        }
    }

    /// <summary>
    /// 切れた・心拍が来ない、をグラフに残す（0 の点）。
    /// 手で切った・線が切れた・心拍なし・0 bpm（計れていない）のどれでも呼ぶ。線は 0 まで落ち、0 のまま、次の 1 拍で上がる（<see cref="Add"/> がもう 1 点置く）。
    /// <para>
    /// <b>最小 / 最大 / 平均には数えない</b>（0 は計れていない時間。記録の DB にも入れない）。
    /// 続けて呼んでも 1 点だけ。まだ 1 点も無ければ置かない（落ちる元が無い）。
    /// </para>
    /// </summary>
    public void Gap(DateTimeOffset at)
    {
        lock (_gate)
        {
            if (_inGap || _samples.Count == 0) return;

            Enqueue(new HeartRateSample(0, at));
            _inGap = true;
        }
    }

    private void Enqueue(HeartRateSample sample)
    {
        _samples.Enqueue(sample);
        while (_samples.Count > capacity) _samples.Dequeue();
    }

    /// <summary>古い順。グラフはそのまま左から右へ描ける。切れていた所は 0（<see cref="Gap"/>）。</summary>
    public IReadOnlyList<HeartRateSample> Recent
    {
        get { lock (_gate) return [.. _samples]; }
    }

    /// <summary>
    /// この繋がりの最小 / 最大 / 平均（<see cref="RestartStats"/> から数える）。
    /// <b><see cref="Recent"/> の上限には縛られない</b>（上限を超えて捨てた分も数に入っている）。
    /// </summary>
    public HeartRateStats Overall
    {
        get
        {
            lock (_gate)
                return _count == 0 ? default : new HeartRateStats(_min, _max, (int)(_sum / _count));
        }
    }

    /// <summary>
    /// 最小 / 最大 / 平均<b>だけ</b>を 0 から数え直す。繋がるたびに呼ぶ。
    /// <para>
    /// <b>直近のグラフは残す。</b> 同じ人がベルトを付け直したり時計へ乗り換えたりしただけなので、
    /// 線が消えると何が起きたのか分からない。
    /// 繋ぎ先そのものを変えたときは <see cref="Clear"/>。
    /// </para>
    /// </summary>
    public void RestartStats()
    {
        lock (_gate)
        {
            _sum = _count = 0;
            _min = _max = 0;
        }
    }

    /// <summary>繋ぎ先を変えたときに畳む。前のデバイスの値を後ろに繋げても意味が無い。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _samples.Clear();
            _inGap = false;
            _sum = _count = 0;
            _min = _max = 0;
        }
    }
}
