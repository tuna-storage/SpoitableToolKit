using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

namespace SpoitableToolKit.Host.Ui.Vr.Overlays;

internal static class VrSprites
{
    public const int DotSize = 32;

    /// <summary>点は白い丸に暗い縁（板が明るくても暗くても見えるように）。縁の外はぼかして透かす。</summary>
    public static (byte[] Pixels, int Size) Draw(VrSprite sprite) => sprite switch
    {
        VrSprite.Dot => (Dot(), DotSize),
        _ => throw new ArgumentOutOfRangeException(nameof(sprite), sprite, null),
    };

    private static byte[] Dot()
    {
        var pixels = new byte[DotSize * DotSize * 4];
        var center = (DotSize - 1) / 2f;

        for (var y = 0; y < DotSize; y++)
        {
            for (var x = 0; x < DotSize; x++)
            {
                var distance = MathF.Sqrt((x - center) * (x - center) + (y - center) * (y - center));

                var (shade, alpha) = distance switch
                {
                    <= 8f => (255, 255),
                    <= 12f => (40, 230),
                    <= 14f => (40, (int)(230 * (14f - distance) / 2f)),
                    _ => (0, 0),
                };

                var at = (y * DotSize + x) * 4;
                pixels[at] = pixels[at + 1] = pixels[at + 2] = (byte)shade;
                pixels[at + 3] = (byte)alpha;
            }
        }

        return pixels;
    }
}
