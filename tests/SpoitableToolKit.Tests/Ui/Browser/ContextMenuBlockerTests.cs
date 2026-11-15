using CefSharp;
using SpoitableToolKit.Host.Ui.Browser;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Browser;

/// <summary>model を空にするところまでを見る。実際にメニューが出ないことは試験にできない（ネイティブのメニューは CEF が OS 側に描くので DOM は変わらない）。</summary>
public class ContextMenuBlockerTests
{
    [Fact]
    public void TheDefaultMenuIsEmptiedBeforeItIsShown()
    {
        var model = new ExplodingMenuModel();

        new ContextMenuBlocker().OnBeforeContextMenu(null!, null!, null!, null!, model);

        Assert.Equal(1, model.Cleared);
    }

    /// <summary><c>true</c> を返すとコールバックを呼ぶまで CEF が待つので、描かないこちらは <c>true</c> を返さない。</summary>
    [Fact]
    public void DrawingIsNotTakenOverFromCef()
    {
        var handled = new ContextMenuBlocker()
            .RunContextMenu(null!, null!, null!, null!, new ExplodingMenuModel(), null!);

        Assert.False(handled);
    }

    [Fact]
    public void NoCommandIsClaimed()
    {
        var handled = new ContextMenuBlocker()
            .OnContextMenuCommand(null!, null!, null!, null!, CefMenuCommand.Copy, CefEventFlags.None);

        Assert.False(handled);
    }

    /// <summary><see cref="Clear"/> 以外を触ったら投げる。数えるだけの偽物だと「空にしてから足し直す」実装でも緑になる。</summary>
    private sealed class ExplodingMenuModel : IMenuModel
    {
        public int Cleared { get; private set; }

        public int Count => throw Boom();

        public bool Clear()
        {
            Cleared++;
            return true;
        }

        public bool AddCheckItem(CefMenuCommand commandId, string label) => throw Boom();

        public bool AddItem(CefMenuCommand commandId, string label) => throw Boom();

        public bool AddRadioItem(CefMenuCommand commandId, string label, int groupId) => throw Boom();

        public bool AddSeparator() => throw Boom();

        public IMenuModel? AddSubMenu(CefMenuCommand commandId, string label) => throw Boom();

        public bool GetAccelerator(
            CefMenuCommand commandId, out int keyCode, out bool shiftPressed, out bool ctrlPressed, out bool altPressed)
            => throw Boom();

        public bool GetAcceleratorAt(
            int index, out int keyCode, out bool shiftPressed, out bool ctrlPressed, out bool altPressed)
            => throw Boom();

        public CefMenuCommand GetCommandIdAt(int index) => throw Boom();

        public int GetGroupId(CefMenuCommand commandId) => throw Boom();

        public int GetGroupIdAt(int index) => throw Boom();

        public int GetIndexOf(CefMenuCommand commandId) => throw Boom();

        public string GetLabel(CefMenuCommand commandId) => throw Boom();

        public string GetLabelAt(int index) => throw Boom();

        public IMenuModel? GetSubMenu(CefMenuCommand commandId) => throw Boom();

        public IMenuModel? GetSubMenuAt(int index) => throw Boom();

        public MenuItemType GetType(CefMenuCommand commandId) => throw Boom();

        public MenuItemType GetTypeAt(int index) => throw Boom();

        public bool HasAccelerator(CefMenuCommand commandId) => throw Boom();

        public bool HasAcceleratorAt(int index) => throw Boom();

        public bool InsertCheckItemAt(int index, CefMenuCommand commandId, string label) => throw Boom();

        public bool InsertItemAt(int index, CefMenuCommand commandId, string label) => throw Boom();

        public bool InsertRadioItemAt(int index, CefMenuCommand commandId, string label, int groupId) => throw Boom();

        public bool InsertSeparatorAt(int index) => throw Boom();

        public IMenuModel? InsertSubMenuAt(int index, CefMenuCommand commandId, string label) => throw Boom();

        public bool IsChecked(CefMenuCommand commandId) => throw Boom();

        public bool IsCheckedAt(int index) => throw Boom();

        public bool IsEnabled(CefMenuCommand commandId) => throw Boom();

        public bool IsEnabledAt(int index) => throw Boom();

        public bool IsVisible(CefMenuCommand commandId) => throw Boom();

        public bool IsVisibleAt(int index) => throw Boom();

        public bool Remove(CefMenuCommand commandId) => throw Boom();

        public bool RemoveAccelerator(CefMenuCommand commandId) => throw Boom();

        public bool RemoveAcceleratorAt(int index) => throw Boom();

        public bool RemoveAt(int index) => throw Boom();

        public bool SetAccelerator(CefMenuCommand commandId, int keyCode, bool shiftPressed, bool ctrlPressed, bool altPressed) => throw Boom();

        public bool SetAcceleratorAt(int index, int keyCode, bool shiftPressed, bool ctrlPressed, bool altPressed) => throw Boom();

        public bool SetChecked(CefMenuCommand commandId, bool @checked) => throw Boom();

        public bool SetCheckedAt(int index, bool @checked) => throw Boom();

        public bool SetCommandIdAt(int index, CefMenuCommand commandId) => throw Boom();

        public bool SetEnabled(CefMenuCommand commandId, bool enabled) => throw Boom();

        public bool SetEnabledAt(int index, bool enabled) => throw Boom();

        public bool SetGroupId(CefMenuCommand commandId, int groupId) => throw Boom();

        public bool SetGroupIdAt(int index, int groupId) => throw Boom();

        public bool SetLabel(CefMenuCommand commandId, string label) => throw Boom();

        public bool SetLabelAt(int index, string label) => throw Boom();

        public bool SetVisible(CefMenuCommand commandId, bool visible) => throw Boom();

        public bool SetVisibleAt(int index, bool visible) => throw Boom();

        private static InvalidOperationException Boom([System.Runtime.CompilerServices.CallerMemberName] string member = "")
            => new($"止めるだけのはずが {member} を触った");
    }
}
