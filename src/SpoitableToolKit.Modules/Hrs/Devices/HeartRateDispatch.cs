using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Modules.Hrs.Events.HeartRate;

namespace SpoitableToolKit.Modules.Hrs.Devices;

/// <summary>
/// 窓口の出来事を、<b>積まれた順に 1 つずつ</b>配る（順番待ちの列）。
/// <para>
/// 積む（<see cref="Enqueue"/>）のは、窓口が状態を決めた lock の中で。配る（<see cref="Drain"/>）のは、その lock の外で。
/// 配るのは一度に 1 人だけ。ほかのスレッドが配っている最中なら、配り終わるのを待ってから、残りを配る —
/// <b>戻ったときには、自分が積んだ知らせは配り終わっている</b>（知らせた側から見ると同期）。
/// 配っている最中の listener が呼び返した（同じスレッドの）ときだけは待たずに戻り、外の回が続けて配る（詰まらない・順も崩れない）。
/// </para>
/// <para>
/// listener は、配っている間に<b>入口のスレッドも、パネルのボタンのスレッドも待たない</b>こと（入口のスレッドは配り終わるのを待っていることがあり、
/// 「切断」「接続」は配り終わるまで戻らない — 窓口の <c>_holdGate</c> を持ったまま。いまの受け手はパネルへ投げるだけ・ログへ積むだけで、誰も待たない）。
/// </para>
/// </summary>
internal sealed class HeartRateDispatch(IEventHub hub)
{
    private readonly Lock _queueGate = new();
    private readonly Lock _deliverGate = new();
    private readonly Queue<HeartRateEventParams> _queue = new();

    /// <summary>いま配っているスレッド（配っていなければ 0）。呼び返しを見分ける。</summary>
    private int _deliveringThread;

    /// <summary>積む。ほかの lock の中から呼んでよい（配らない）。</summary>
    public void Enqueue(HeartRateEventParams e)
    {
        lock (_queueGate) _queue.Enqueue(e);
    }

    /// <summary>
    /// 配る。<b>窓口の lock の外で呼ぶ</b>。ほかのスレッドが配っている最中なら、配り終わるのを待ってから残りを配る。
    /// 配っている最中の呼び返しなら、何もせず戻る（外の回が続けて配る）。
    /// </summary>
    public void Drain()
    {
        if (Volatile.Read(ref _deliveringThread) == Environment.CurrentManagedThreadId) return;

        lock (_deliverGate)
        {
            Volatile.Write(ref _deliveringThread, Environment.CurrentManagedThreadId);
            try
            {
                while (TryTake(out var next)) Deliver(next);
            }
            finally
            {
                Volatile.Write(ref _deliveringThread, 0);
            }
        }
    }

    private bool TryTake(out HeartRateEventParams next)
    {
        lock (_queueGate) return _queue.TryDequeue(out next!);
    }

    private void Deliver(HeartRateEventParams e)
    {
        switch (e)
        {
            case HeartRateConnectedEventParams connected:
                hub.Fire<IHeartRateEventListener>(l => l.OnConnected(connected));
                break;

            case HeartRateDisconnectedEventParams disconnected:
                hub.Fire<IHeartRateEventListener>(l => l.OnDisconnected(disconnected));
                break;

            case HeartBeatEventParams beat:
                hub.Fire<IHeartRateEventListener>(l => l.OnHeartBeat(beat));
                break;

            case HeartRateSignalLostEventParams lost:
                hub.Fire<IHeartRateEventListener>(l => l.OnSignalLost(lost));
                break;

            case HeartRateSignalBackEventParams back:
                hub.Fire<IHeartRateEventListener>(l => l.OnSignalBack(back));
                break;
        }
    }
}
