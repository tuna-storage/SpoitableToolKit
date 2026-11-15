using SpoitableToolKit.Host.Ui.Vr.Overlays;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Vr.Overlays;

public class VrSpritesTests
{
    /// <summary>点は白い芯に暗い縁（明るい板でも暗い板でも見える）、角は透明。</summary>
    [Fact]
    public void TheDotIsAWhiteCoreWithADarkRimAndClearCorners()
    {
        var (pixels, size) = VrSprites.Draw(VrSprite.Dot);

        Assert.Equal(VrSprites.DotSize, size);
        Assert.Equal(size * size * 4, pixels.Length);

        byte[] At(int x, int y) => pixels[((y * size + x) * 4)..((y * size + x) * 4 + 4)];

        Assert.Equal([255, 255, 255, 255], At(size / 2, size / 2));
        Assert.True(At(size / 2 + 10, size / 2)[0] < 100, "縁が暗くない");
        Assert.True(At(size / 2 + 10, size / 2)[3] > 200, "縁が透けている");
        Assert.Equal(0, At(0, 0)[3]);
        Assert.Equal(0, At(size - 1, size - 1)[3]);
    }
}
