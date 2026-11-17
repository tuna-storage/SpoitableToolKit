namespace SpoitableToolKit.Host.Ui.Tray;

/// <summary>
/// トレイのメニューの項目に、Windows のメニューへ渡す番号を振り、返ってきた番号を項目の名前へ戻す。Win32 を知らない形にしてある（試験はここを見る）。
/// 番号は 1 から、押せる項目（区切りでも、サブメニューの親でもないもの）にだけ、上から（サブメニューは親の位置で潜って）順に振る。
/// 0 は「何も押さなかった」（メニューの外を押した・Esc）で、どの項目にも当たらない。
/// </summary>
internal sealed class TrayCommands
{
    private readonly Dictionary<TrayMenuItem, int> _numbers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, string> _ids = [];

    private TrayCommands()
    {
    }

    public static TrayCommands For(IReadOnlyList<TrayMenuItem> items)
    {
        var commands = new TrayCommands();
        commands.Number(items);
        return commands;
    }

    private void Number(IReadOnlyList<TrayMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.Separator) continue;

            if (item.Children.Count > 0)
            {
                Number(item.Children);
                continue;
            }

            var number = _ids.Count + 1;
            _numbers[item] = number;
            _ids[number] = item.Id;
        }
    }

    /// <summary>その項目の番号。番号を振らない項目（区切り・サブメニューの親）と、このメニューに無い項目は 0。</summary>
    public int NumberOf(TrayMenuItem item) => _numbers.GetValueOrDefault(item);

    /// <summary>返ってきた番号の項目の名前。0（何も押さなかった）と、振っていない番号は null。</summary>
    public string? IdOf(int command) => _ids.GetValueOrDefault(command);
}
