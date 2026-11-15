namespace SpoitableToolKit.Abstractions.Ui;

/// <summary>
/// コンテキストメニュー（右クリック、VR では長押し）に項目を足す口。<b><see cref="IModuleUi.RegisterContextMenu"/> で登録したときだけ聞かれる</b>
/// （モジュールのクラスが実装しているだけでは聞かれない。別のクラスに実装して登録してもよい）。
/// <para>
/// <b>帰属はホストが決める。</b> モジュールが「自分は誰々だ」と名乗る余地は無く、
/// 呼び出しは発信元の iframe の origin から宛先を決めている。
/// 自分のページを持たないモジュールの上ではメニューが開かないので、登録しても呼ばれない。
/// </para>
/// <para>
/// <b>投げてよい。</b> 1 つのモジュールの失敗でメニューごと壊さない
/// （そのモジュールの項目だけが落ちる）。
/// </para>
/// </summary>
public interface IContextMenuSource
{
    /// <summary>
    /// この場所に出す項目。<b>押された瞬間に呼ばれる</b>ので、
    /// そのときの状態をそのまま見てよい（配っておいた表ではないので、古くならない）。
    /// </summary>
    IReadOnlyList<ContextMenuItem> GetContextMenu(ContextMenuTarget target);

    /// <summary>
    /// 項目が押された。<paramref name="id"/> は <see cref="ContextMenuItem.Id"/> がそのまま返る
    /// （ホストが付けた持ち主は外れている）。
    /// </summary>
    Task InvokeContextMenuAsync(string id, ContextMenuTarget target, CancellationToken cancellationToken = default);
}
