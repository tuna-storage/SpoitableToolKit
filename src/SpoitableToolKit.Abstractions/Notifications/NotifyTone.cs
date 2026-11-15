namespace SpoitableToolKit.Abstractions.Notifications;

/// <summary>
/// 通知の角四角の色の系統。<b>実際の色はテーマが決める</b>（<c>--tone-*</c>）ので、dark / light で勝手に変わる。
/// <para>
/// 色を直に渡す口にしないのは、<b>明るいテーマで沈む色をモジュールが選べてしまう</b>から。
/// 系統だけ選んでもらえば、どのテーマでも読める濃さはテーマの側が持つ。
/// </para>
/// </summary>
public enum NotifyTone
{
    Gray,
    Accent,
    Green,
    Red,
    Amber,
    Blue,
    Purple,
    Teal,
    Rose,
}
