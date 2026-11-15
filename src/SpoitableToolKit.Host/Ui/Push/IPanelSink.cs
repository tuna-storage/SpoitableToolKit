namespace SpoitableToolKit.Host.Ui.Push;

internal interface IPanelSink
{
    /// <summary>JSON として読めないものは捨てる。</summary>
    void Push(string channel, string payloadJson);

    /// <summary>サイドバーの無い面では何も起きない。</summary>
    void Show();

    /// <summary>view は <c>&lt;Id&gt;</c> か <c>&lt;Id&gt;#&lt;場所&gt;</c>。出していれば閉じる。VR が動いていなければ何も起きない。</summary>
    void ToggleVrPanel(string view);
}
