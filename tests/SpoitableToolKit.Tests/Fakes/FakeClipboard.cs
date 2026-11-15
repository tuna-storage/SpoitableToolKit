using SpoitableToolKit.Abstractions.Clipboard;

namespace SpoitableToolKit.Tests.Fakes;

internal sealed class FakeClipboard : IClipboard
{
    public string? Text { get; set; }

    public bool Writable { get; set; } = true;

    public bool SetText(string text)
    {
        if (!Writable) return false;

        Text = text;
        return true;
    }

    public string? GetText() => Text;
}
