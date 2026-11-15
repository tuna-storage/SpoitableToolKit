using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Modules.Hrs.Broadcast;

/// <summary>
/// 配信の待ち受け（<see cref="HrsBroadcastServer"/>）を開く・開き直す・畳む。心拍の入口とは寿命を分ける
/// （デバイスを変えるたびに切ると、OBS のブラウザソースが真っ黒になって貼り直す羽目になる）。
/// </summary>
internal sealed class HrsBroadcastRunner(Func<HrsBroadcastServer?> server, Func<ILogger> logger)
{
    private CancellationTokenSource? _running;
    private Task _loop = Task.CompletedTask;

    /// <summary>
    /// 配信を開き直す。前の周回の後片付けを待ってから次を始める
    /// （待たずに始めると、前の <c>HttpListener</c> がポートを離す前に次が掴みに行く）。
    /// </summary>
    public void Restart()
    {
        var previous = StopAsync();

        if (server() is not { } broadcast) return;

        var running = new CancellationTokenSource();
        _running = running;

        _loop = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);

            try
            {
                await broadcast.RunAsync(running.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger().LogWarning(ex, "Broadcasting stopped");
            }
        });
    }

    /// <summary>畳む。戻り値を待つと、ポートを離し終わるまで見られる。</summary>
    public Task StopAsync()
    {
        var running = _running;
        var loop = _loop;

        _running = null;
        _loop = Task.CompletedTask;

        if (running is null) return loop;

        running.Cancel();
        return loop.ContinueWith(_ => running.Dispose(), TaskScheduler.Default);
    }
}
