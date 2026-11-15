using System.Globalization;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Abstractions.Shortcuts;

namespace SampleModule;

/// <summary>
/// いちばん小さいモジュール。e2e が「外から入れて、次の起動で読み込まれる」を本物のアプリで見るのに使う
/// （zip にして、手元の偽の GitHub から落とさせる）。
/// <para>
/// 手首にドラッグだけのタイル（<c>sample/knob</c>）を 1 つ登録する。同梱のモジュールにはドラッグだけのタイルが無いので、
/// e2e がドラッグの道を本物のアプリで通すのに使う（始めと終わりをトーストで知らせる）。
/// </para>
/// </summary>
public sealed class SampleModule : ISpoitableModule
{
    public bool OnInit(IModuleHost host)
    {
        host.Shortcuts.Register(new Knob(host.Notifications));
        return true;
    }

    /// <summary>
    /// 自分で持ってきたファイル（<c>data/shipped.txt</c>。csproj が出力へ配る）を、自分のフォルダから開いて読む。試験が型越しに呼ぶ。
    /// <see cref="OnInit"/> では読まない — e2e の zip は dll と deps.json だけなので、そこで読むと e2e で入れたものが読み込めなくなる。
    /// </summary>
    public string ReadShippedFile() => File.ReadAllText(Path.Combine(ModuleInfo.ModuleDirectory, "data/shipped.txt"));

    /// <summary>ドラッグだけのタイル。始めと終わりの位置をトーストに出す（途中は出さない）。</summary>
    private sealed class Knob(INotificationManager notify) : IDraggableWristShortcut
    {
        public string Id => "knob";

        public string Name => "Knob";

        public Task OnStartDragAsync(ShortcutDragEventParams e, CancellationToken cancellationToken)
        {
            notify.PushToast("knob start", Format(e));
            return Task.CompletedTask;
        }

        public Task OnDraggingAsync(ShortcutDragEventParams e, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnEndDragAsync(ShortcutDragEventParams e, CancellationToken cancellationToken)
        {
            notify.PushToast("knob end", Format(e));
            return Task.CompletedTask;
        }

        private static string Format(ShortcutDragEventParams e) =>
            string.Create(CultureInfo.InvariantCulture, $"{e.Position.X:0.00},{e.Position.Y:0.00} from {e.StartPosition.X:0.00},{e.StartPosition.Y:0.00}");
    }
}
