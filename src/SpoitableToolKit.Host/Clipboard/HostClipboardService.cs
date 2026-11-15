using SpoitableToolKit.Abstractions.Clipboard;

namespace SpoitableToolKit.Host.Clipboard;

internal sealed class HostClipboardService : IClipboard
{
    public bool SetText(string text) => HostClipboard.SetText(text);

    public string? GetText() => HostClipboard.GetText();
}
