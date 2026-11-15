using SpoitableToolKit.Abstractions.Notifications;

namespace SpoitableToolKit.Tests.Fakes;

internal sealed class FakeNotifications : INotificationManager
{
    public List<NotifyRequest> Asked { get; } = [];

    public List<(string Title, string? Body)> Toasts { get; } = [];

    public bool Accept { get; set; }

    public Task<bool> PushModalAsync(NotifyRequest request, CancellationToken cancellationToken = default)
    {
        Asked.Add(request);
        return Task.FromResult(Accept);
    }

    public List<(string Title, string? Body)> Banners { get; } = [];

    public List<NotifyKind> Kinds { get; } = [];

    public void PushBanner(NotifyKind kind, string title, string? body = null, params NotifyAction[] actions)
    {
        Kinds.Add(kind);
        Banners.Add((title, body));
    }

    public IToast PushToast(NotifyKind kind, string title, string? body = null, params NotifyAction[] actions)
    {
        Kinds.Add(kind);
        Toasts.Add((title, body));

        return new FakeToast { Title = title, Body = body };
    }
}
