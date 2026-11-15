namespace SpoitableToolKit.Abstractions.Shortcuts;

/// <summary>
/// 手首のショートカットを本体に渡す口（<see cref="IModuleHost.Shortcuts"/>）。モジュール 1 つに 1 つあり、登録したものは自分の分として並ぶ。
/// <para>
/// <b>画面まわり（<see cref="IModuleHost.Ui"/>）とは別の口</b> — ショートカットは表示だけでなく、押されたときの処理も持つ。
/// <b>本体が並べるのは、ここで登録したものだけ</b>（モジュールのクラスが <see cref="IWristShortcut"/> を実装しているだけでは並ばない）。
/// </para>
/// <para>
/// タイルは 2 種類で、登録の口も分かれている: 押すだけの <see cref="IClickableWristShortcut"/> と、ドラッグだけの <see cref="IDraggableWristShortcut"/>。
/// 両方を実装したクラスは、どちらの口に渡すか決まらないので build で落ちる（型を絞って渡しても、登録のときに例外）。
/// </para>
/// <para>
/// 登録できるのは <b><see cref="ISpoitableModule.OnInit"/> の中だけ</b>（いくつでもよい。並ぶ順は登録した順。2 種類は混ぜて並べてよい）。
/// 外れたら例外で断る: OnInit の外は <see cref="InvalidOperationException"/>、同じ <see cref="IWristShortcut.Id"/> の 2 回目も
/// <see cref="InvalidOperationException"/>、<see cref="IWristShortcut.Id"/>・<see cref="IWristShortcut.Name"/>・<see cref="IWristShortcut.Page"/>・
/// <see cref="IWristShortcut.MarkedPanel"/> の字が外れている・両方の種類を実装している、は <see cref="ArgumentException"/>。どれも何を直せばよいかを文で返す。
/// </para>
/// </summary>
public interface IModuleShortcuts
{
    /// <summary>押すだけのショートカットを 1 つ登録する（決まりは <see cref="IModuleShortcuts"/>）。</summary>
    void Register(IClickableWristShortcut shortcut);

    /// <summary>ドラッグだけのショートカットを 1 つ登録する（決まりは <see cref="IModuleShortcuts"/>）。</summary>
    void Register(IDraggableWristShortcut shortcut);
}
