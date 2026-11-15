using SpoitableToolKit.Host.Ui.Push;

namespace SpoitableToolKit.Tests.Fakes;

internal sealed class FakePanelChannel : IPanelSink
{
    public List<(string Channel, string Payload)> Pushed { get; } = [];

    public int Shown { get; private set; }

    public void Push(string channel, string payloadJson)
    {
        lock (Pushed) Pushed.Add((channel, payloadJson));
    }

    public void Show() => Shown++;

    public List<string> Toggled { get; } = [];

    public void ToggleVrPanel(string view)
    {
        lock (Toggled) Toggled.Add(view);
    }
}
