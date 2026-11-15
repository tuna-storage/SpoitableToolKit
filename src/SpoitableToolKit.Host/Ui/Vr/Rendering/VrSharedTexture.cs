using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Ui.Vr.Diagnostics;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SpoitableToolKit.Host.Ui.Vr.Rendering;

/// <summary>
/// VR に出す絵の GPU 側。自前のテクスチャを 1 枚持ち、CEF の絵をそこへ写し取る。
/// CEF のテクスチャをそのまま渡す道は、<c>SetOverlayTexture</c> が <c>None</c>（成功）を返すのにヘッドセットには何も出なかった。容疑は 3 つあり、写し取る形にすると 3 つとも同時に消える:
/// 1. NT ハンドル。CEF がくれるのは NT ハンドルの共有テクスチャで、SteamVR のコンポジタは旧来の共有ハンドル（<c>IDXGIResource::GetSharedHandle</c>）で取り込もうとし、NT ハンドルの資源では取れない。自前の 1 枚は旧来の共有（<c>MiscFlags = Shared</c>）で作る。
/// 2. 毎フレーム作り直される。CEF はフレームごとに新しい共有テクスチャを作り、コールバックが返ったら閉じる。渡した先がいつまで読むかは分からない。
/// 3. こちらが捨てた先を読まれる。輪で 3 枚生かしていても、SteamVR がそれより後に読めば消えた先を読むことになる。
/// 絵は CPU を通らない（GPU の上で写すだけなので、色の入れ替えも要らない: BGRA のまま）。
/// </summary>
/// <param name="width">板の画素数。面ごとに違う（ダッシュボードと手首）。</param>
internal sealed class VrSharedTexture(ILogger logger, int width = VrSurface.Width, int height = VrSurface.Height) : IDisposable
{
    /// <summary>試すアダプタの上限。この台数を超える機械は想定しない。</summary>
    private const int MaxAdapters = 4;

    /// <summary>生きているデバイスで開けない状態がこれだけ続いたら、デバイスを探し直す。1 回開けないだけで捨てると、捨てては作り直すことになる（<see cref="Copy"/> の注記）。かといって探し直さないと、CEF が別のアダプタへ移ったときにずっと飛ばし続ける。</summary>
    private const long GiveUpOnTheDeviceAfterMs = 5_000;

    /// <summary>アダプタを総当たりする間隔の下限。どこでも開けない間に毎フレーム総当たりすると、フレームごとにデバイスを最大 4 つ作っては捨てる（錠を持ったまま）。</summary>
    private const long SearchAtMostEveryMs = 1_000;

    /// <summary>写すのは CEF のスレッド、渡すのは UI のスレッド。D3D のコンテキストは共有できないので、触るところをまとめて塞ぐ。渡すところ（<see cref="Lend"/>）も同じ錠の中。</summary>
    private readonly object _gate = new();

    private ID3D11Device? _device;

    private ID3D11Device1? _device1;

    private ID3D11DeviceContext? _context;

    /// <summary>OpenVR へ渡す 1 枚。ずっと同じものを指す。</summary>
    private ID3D11Texture2D? _own;

    private long _sequence;

    private bool _describedSource;

    private bool _mutexFailed;

    /// <summary>開けない状態が続いているか。ログは入るときと抜けるときの 1 回ずつ。</summary>
    private bool _failing;

    /// <summary>生きているデバイスで開けなくなった時刻（<see cref="Environment.TickCount64"/>）。0 は「開けている」。</summary>
    private long _failingSince;

    private long _skipped;

    private long _nextSearchAt;

    /// <summary>渡す 1 枚を作れなかったことをもう残したか。毎フレーム出さないため。</summary>
    private bool _ownFailed;

    /// <summary>片付け済み。このあとに届いた絵では何も作らない。browser の Dispose は閉じ始めるだけなので片付けたあとにも描画が届きうるが、印が無いとそこでデバイスと渡す 1 枚を作り直し、誰も捨てない。</summary>
    private bool _disposed;

    /// <summary>これまでに写し取った枚数（測る用）。</summary>
    public long Sequence
    {
        get
        {
            lock (_gate) return _sequence;
        }
    }

    /// <summary>
    /// いま出すべき絵を貸す。テクスチャがまだ無ければ <see cref="VrFrame.None"/>。
    /// OpenVR へ渡すのは <paramref name="use"/> の中で済ませること — 貸している間は錠を持っているので、CEF のスレッドは写せないし捨てられない。
    /// 錠の外で渡すと、その隙間に CEF のスレッドがテクスチャを捨て、捨てた先を <c>SetOverlayTexture</c> に渡して落ちる（<c>CVROverlay.SetOverlayTexture</c> の中で 0xC0000005）。
    /// 錠の中で渡すのにはもう 1 つ理由がある: OpenVR は渡されたテクスチャのデバイスで自分用の写しを作る（vrclient のログ: <c>Created shared texture 'Scene create D3D11, -163' 1280x720</c>）ので、同じデバイスのコンテキストを UI のスレッドから触ることになり、CEF のスレッドの写しと重なってはいけない。
    /// ここは事実を貸すだけで、「まだ 1 枚も写していないものを渡してよいか」の判断は <see cref="VrHost"/> が持つ（<c>Sequence</c> が 0 のときがそれ）。
    /// </summary>
    public void Lend(Action<VrFrame> use)
    {
        lock (_gate)
        {
            use(_own is null ? VrFrame.None : new VrFrame(_own.NativePointer, _sequence));
        }
    }

    /// <summary>
    /// CEF の共有ハンドルを開いて、自前の 1 枚へ写し取る。
    /// ⚠ CEF のコールバックの中から呼ぶこと。CEF はフレームごとに新しい共有テクスチャを作り、コールバックが返ったら閉じる（あとから開くと <c>E_INVALIDARG</c>）。
    /// ⚠ 1 回開けなかっただけでは、デバイスも渡す 1 枚も捨てない。そのフレームを飛ばすだけで、板には前の絵が残る。開けないたびに丸ごと捨てて作り直すと、渡す 1 枚が作り直されるたびに vrclient がこちらの板の寸法で写しを作り直して落ちる。
    /// 捨てるのはデバイスが本当に失われたとき（GPU ドライバのリセット）と、<see cref="GiveUpOnTheDeviceAfterMs"/> 開けない状態が続いたときだけ。
    /// </summary>
    public void Copy(nint shared)
    {
        if (shared == 0) return;

        lock (_gate)
        {
            if (_disposed) return;

            if (_device is not null)
            {
                if (TryOpen(shared, out var reason) is { } source)
                {
                    using (source)
                    {
                        Opened();
                        Describe(source);

                        if (EnsureOwn()) Blit(source);
                    }

                    return;
                }

                var removed = _device.DeviceRemovedReason;

                if (removed.Success && !FailingTooLong())
                {
                    Skip(reason);
                    return;
                }

                if (removed.Failure)
                {
                    logger.LogWarning("The D3D11 device for the VR overlay was lost ({Reason}); looking for one again", removed.ToString());
                }
                else
                {
                    logger.LogWarning("The CEF texture has not opened for a while ({Reason}); looking for another adapter", reason);
                }

                Release();
            }

            Search(shared);
        }
    }

    /// <summary>アダプタを総当たりする。CEF の GPU プロセスが作ったテクスチャは、同じアダプタ上のデバイスからしか開けない。どれを使っているかは聞けない。</summary>
    private void Search(nint shared)
    {
        var now = Environment.TickCount64;

        if (now < _nextSearchAt)
        {
            _skipped++;
            return;
        }

        string? reason = null;

        for (var adapter = 0; adapter < MaxAdapters; adapter++)
        {
            if (!EnsureDevice(adapter))
            {
                Release();
                continue;
            }

            if (TryOpen(shared, out reason) is not { } source)
            {
                Release();
                continue;
            }

            using (source)
            {
                Opened();
                Describe(source);

                if (EnsureOwn()) Blit(source);
            }

            return;
        }

        _nextSearchAt = now + SearchAtMostEveryMs;
        Skip(reason ?? "no adapter could make a device");
    }

    /// <summary>このフレームは飛ばす。ログは開けなくなった最初の 1 回だけ — 毎フレーム出すと、その行がアプリログの欄へ流れて再描画を起こし、それがまた次の行を生む。</summary>
    private void Skip(string reason)
    {
        _skipped++;

        if (_failingSince == 0) _failingSince = Environment.TickCount64;

        if (_failing) return;

        _failing = true;
        logger.LogWarning("Could not open the CEF texture ({Reason}); the VR overlay keeps its last picture until it opens again", reason);
    }

    private void Opened()
    {
        if (_failing)
        {
            logger.LogInformation("The CEF texture opens again ({Skipped} frames were skipped)", _skipped);
        }

        _failing = false;
        _failingSince = 0;
        _skipped = 0;
        _nextSearchAt = 0;
    }

    private bool FailingTooLong() =>
        _failingSince != 0 && Environment.TickCount64 - _failingSince >= GiveUpOnTheDeviceAfterMs;

    /// <summary>写し取る。鍵が付いていれば取ってから — 付いたまま読むと、CEF がまだ描いている途中の絵を掴む。</summary>
    private void Blit(ID3D11Texture2D source)
    {
        var mutex = source.QueryInterfaceOrNull<IDXGIKeyedMutex>();

        if (mutex is null)
        {
            _context!.CopyResource(_own!, source);
        }
        else
        {
            using (mutex)
            {
                // 鍵は 0。Chromium が共有する絵はこの流儀で渡ってくる。
                try
                {
                    mutex.AcquireSync(0, 16);
                }
                catch (Exception ex)
                {
                    if (_mutexFailed) return;

                    _mutexFailed = true;
                    logger.LogWarning("Could not take the lock on the CEF texture ({Message}); that frame is skipped", ex.Message);

                    return;
                }

                try
                {
                    _context!.CopyResource(_own!, source);
                }
                finally
                {
                    mutex.ReleaseSync(0);
                }
            }
        }

        // ⚠ 流し込む。このデバイスは 1 度も Present しないので、これが無いと写しが GPU へ出されないまま溜まる（「全部成功しているのに真っ黒」になる）。
        _context!.Flush();

        _sequence++;
    }

    /// <summary>CEF が寄こすテクスチャの実際の形を 1 回だけ残す。<c>misc=Shared, SharedNTHandle</c> と出て、NT ハンドルの資源を SteamVR がそのままでは取り込めないことが分かったので、同じ顔の問題を次に見るとき推測せずここを見るため。</summary>
    private void Describe(ID3D11Texture2D source)
    {
        if (_describedSource) return;

        _describedSource = true;

        var description = source.Description;

        logger.LogDebug(
            "vr/cef texture {Width}x{Height} format={Format} misc={Misc} usage={Usage} bind={Bind} samples={Samples}",
            description.Width, description.Height, description.Format, description.MiscFlags,
            description.Usage, description.BindFlags, description.SampleDescription.Count);
    }

    /// <summary>開く。開けなかった理由は <paramref name="reason"/> に入る。ここではログに出さない（毎フレーム呼ばれる。出すのは <see cref="Skip"/> が状態の変わり目に 1 回だけ）。</summary>
    private ID3D11Texture2D? TryOpen(nint shared, out string reason)
    {
        try
        {
            reason = "";

            // NT ハンドルを開くのは ID3D11Device1 の口。旧来の OpenSharedResource では開けない。
            return _device1!.OpenSharedResource1<ID3D11Texture2D>(shared);
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return null;
        }
    }

    /// <summary>渡す 1 枚を作る。旧来の共有で作るのが肝で、ここが NT ハンドルだと SteamVR は受け取ったつもりのまま何も出さない。</summary>
    private bool EnsureOwn()
    {
        if (_own is not null) return true;
        if (_device is null) return false;

        var description = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,

            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,

            // ⚠ 旧来の共有。NT ハンドル（SharedNTHandle）ではない。
            MiscFlags = ResourceOptionFlags.Shared,
        };

        try
        {
            // 中身は空のままでよい。1 枚写し取るまで渡さないので、外から見えることはない。
            _own = _device.CreateTexture2D(description);
            _ownFailed = false;

            return true;
        }
        catch (Exception ex)
        {
            // 次のフレームでまた試すので、残すのは最初の 1 回だけ（毎フレーム出すと再描画の輪になる）。
            if (!_ownFailed) logger.LogError(ex, "Could not create the texture the VR overlay hands over");

            _ownFailed = true;
            return false;
        }
    }

    private bool EnsureDevice(int index)
    {
        if (_device is not null) return true;

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

            if (factory.EnumAdapters1((uint)index, out var adapter).Failure) return false;

            using (adapter)
            {
                // BgraSupport を立てる。CEF が出すのは BGRA なので、無いと開けない。アダプタを指す場合、DriverType は Unknown でなければならない。
                D3D11.D3D11CreateDevice(
                        adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, null!,
                        out ID3D11Device device, out ID3D11DeviceContext context)
                    .CheckError();

                _device = device;
                _context = context;
                _device1 = device.QueryInterface<ID3D11Device1>();
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not create the D3D11 device for the VR overlay");
            return false;
        }
    }

    private void Release()
    {
        _own?.Dispose();
        _device1?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _own = null;
        _device1 = null;
        _context = null;
        _device = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Release();
        }
    }
}
