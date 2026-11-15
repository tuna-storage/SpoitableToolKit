using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Host.Tools;
using Xunit;

namespace SpoitableToolKit.Tests.Notifications;

/// <summary>
/// 取得の本体はネットワーク越しなので試験に掛けられない。勘定だけ切り出してあるので、そちらを見る。
/// </summary>
public sealed class DownloadProgressTests
{
    private sealed class Spy : IToast
    {
        public string Title { get; set; } = "";

        public string? Body { get; set; }

        public List<double?> Reported { get; } = [];

        public List<string?> Bodies { get; } = [];

        public double? Progress
        {
            get;
            set { field = value; Reported.Add(value); }
        }

        public void Dispose()
        {
        }

        public void Note() => Bodies.Add(Body);
    }

    private static async Task<Spy> CopyAsync(int bytes, long? total)
    {
        var toast = new Spy();
        var source = new MemoryStream(new byte[bytes]);
        var destination = new MemoryStream();

        await ExternalToolProvider.CopyWithProgressAsync(source, destination, total, toast, CancellationToken.None);

        Assert.Equal(bytes, destination.Length);
        return toast;
    }

    [Fact]
    public async Task AKnownLengthDrivesAPercentage()
    {
        var toast = await CopyAsync(bytes: 81920 * 4, total: 81920 * 4);

        Assert.Equal(4, toast.Reported.Count);
        Assert.Equal(0.25, toast.Reported[0]);
        Assert.Equal(1, toast.Reported[^1]);
    }

    /// <summary>
    /// GitHub は長さを付けてこないことがある（chunked）。そのときは<b>進み具合を入れない</b>ので、
    /// 代わりに取れた量を出す。<b>入れないと時計が動いたままになる</b>ので 0 は呼び手が先に入れる。
    /// </summary>
    [Fact]
    public async Task AnUnknownLengthReportsTheBytesSoFar()
    {
        var toast = await CopyAsync(bytes: 1024 * 1024 * 3, total: null);

        Assert.Empty(toast.Reported);
        Assert.Equal("3 MB", toast.Body);
    }
}
