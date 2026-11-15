using SpoitableToolKit.Abstractions.Notifications;

namespace SpoitableToolKit.Tests.Fakes;

internal sealed class FakeToast : IToast
{
    public string Title { get; set; } = "";

    public string? Body { get; set; }

    public double? Progress { get; set; }

    public void Dispose()
    {
    }
}
