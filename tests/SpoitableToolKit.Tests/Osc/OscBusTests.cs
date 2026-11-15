using SpoitableToolKit.Host.Osc;
using Xunit;

namespace SpoitableToolKit.Tests.Osc;

/// <summary>
/// 実際に踏む仕様は、ビッグエンディアンと 4 バイト境界のパディング。どちらも間違えても送信自体は成功するので、バイト列そのもので見る。
/// </summary>
public sealed class OscBusTests
{
    private static byte[] Bytes(string hex) =>
        [.. hex.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(b => Convert.ToByte(b, 16))];

    [Fact]
    public void IntegerArgumentsAreLaidOutBigEndian()
    {
        var packet = OscBus.Encode("/a", 72);

        Assert.Equal(Bytes(
            // "/a" + 終端 + パディング  |  ",i" + 終端 + パディング  |  72
            "2F 61 00 00" + " " + "2C 69 00 00" + " " + "00 00 00 48"), packet);
    }

    /// <summary>
    /// <c>bool</c> は型タグ（T / F）だけで表す。<b>中身は 1 バイトも書かない。</b>
    /// 0 / 1 を書き足すと、受け取り側は次の引数の頭がずれる。
    /// </summary>
    [Fact]
    public void BooleansAreTypeTagOnlyWithNoPayload()
    {
        Assert.Equal(Bytes("2F 61 00 00 2C 54 00 00"), OscBus.Encode("/a", true));
        Assert.Equal(Bytes("2F 61 00 00 2C 46 00 00"), OscBus.Encode("/a", false));
    }

    /// <summary>
    /// 4 の倍数の長さでも<b>終端のぶんだけ次の境界まで進む</b>。
    /// 「ちょうど収まったからパディング無し」にすると 4 バイトずれる。
    /// </summary>
    [Fact]
    public void ALengthExactlyOnTheBoundaryStillAdvancesForTheTerminator()
    {
        // "/abc" は 4 バイト。終端 1 + パディング 3 で 8 バイトになる。
        var packet = OscBus.Encode("/abc");

        Assert.Equal(Bytes("2F 61 62 63 00 00 00 00" + " " + "2C 00 00 00"), packet);
    }

    /// <summary>引数が無くても型タグの "," は書く。省くと相手が読めない。</summary>
    [Fact]
    public void TheTypeTagIsWrittenEvenWithNoArguments()
    {
        Assert.Equal(Bytes("2F 61 00 00 2C 00 00 00"), OscBus.Encode("/a"));
    }

    [Fact]
    public void FloatsAreAlsoLaidOutBigEndian()
    {
        // 0.5f = 0x3F000000
        Assert.Equal(Bytes("2F 61 00 00 2C 66 00 00 3F 00 00 00"), OscBus.Encode("/a", 0.5f));
    }

    [Fact]
    public void StringArgumentsAreTerminatedAndPadded()
    {
        // "hi" + 終端 + パディング 1 = 4 バイト
        Assert.Equal(Bytes("2F 61 00 00 2C 73 00 00 68 69 00 00"), OscBus.Encode("/a", "hi"));
    }

    [Fact]
    public void SeveralArgumentsPutAllTypeTagsFirst()
    {
        var packet = OscBus.Encode("/avatar/parameters/HR", 72, true, 0.5f);

        Assert.Equal(Bytes(
            // "/avatar/parameters/HR" は 21 バイト。終端 1 + パディング 2 で 24。
            "2F 61 76 61 74 61 72 2F 70 61 72 61 6D 65 74 65 72 73 2F 48 52 00 00 00" + " " +

            // 型タグ ",iTf" は 4 バイト。ここも終端 1 + パディング 3 で 8 になる。
            "2C 69 54 66 00 00 00 00" + " " +

            // 中身は int と float だけ。**bool の分は 1 バイトも入らない。**
            "00 00 00 48" + " " + "3F 00 00 00"), packet);
    }

    /// <summary>
    /// 送れない型は例外。黙って落とすと「送ったのに効かない」の原因が分からない
    /// （<c>double</c> を渡す取り違えが起きやすい）。
    /// </summary>
    [Fact]
    public void AnUnsendableTypeThrows()
    {
        var ex = Assert.Throws<ArgumentException>(() => OscBus.Encode("/a", 1.5d));
        Assert.Contains("Double", ex.Message);
    }
}
