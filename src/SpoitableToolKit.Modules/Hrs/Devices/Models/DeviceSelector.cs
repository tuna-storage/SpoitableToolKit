namespace SpoitableToolKit.Modules.Hrs.Devices.Models;

/// <summary>
/// どの心拍計に繋ぐか。設定の 1 行から作る。
/// <para>
/// アドレスと名前のどちらでも指定できるようにしてあるのは、候補から選べば
/// <c>名前 [アドレス]</c> が入るが、<b>手で名前だけ書く人も居る</b>ため。
/// </para>
/// </summary>
internal readonly record struct DeviceSelector(ulong? Address, string? Name)
{
    /// <summary>指定なし。最初に見つかった心拍計に繋ぐ。</summary>
    public static DeviceSelector Any => default;

    public bool Matches(MonitorIdentity device)
    {
        if (Address is { } address) return device.Address == address;
        if (Name is { } name) return device.Name.Contains(name, StringComparison.OrdinalIgnoreCase);

        return true;
    }

    /// <summary>
    /// 設定の 1 行を読む。<c>[...]</c> があればアドレス、無ければ名前として扱う。
    /// <para>
    /// <b>読めなかったことを呼び出し側へ返す</b>のが肝（<paramref name="error"/> は読めなかった
    /// 文字列そのもので、画面へ出す文は呼び出し側が組む）。設定の変更を却下する理由になるので、
    /// 「読めないから空扱い」で飲み込まない（黙って別のデバイスに繋がるのが一番困る）。
    /// </para>
    /// </summary>
    public static bool TryParse(string? text, out DeviceSelector selector, out string? error)
    {
        selector = Any;
        error = null;

        if (string.IsNullOrWhiteSpace(text)) return true;

        var open = text.LastIndexOf('[');
        var close = text.LastIndexOf(']');

        if (open < 0 || close < open)
        {
            selector = new DeviceSelector(null, text.Trim());
            return true;
        }

        var inside = text[(open + 1)..close];
        var hex = new string([.. inside.Where(char.IsAsciiHexDigit)]);

        if (hex.Length is 0 or > 12 || !ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var address))
        {
            // **読めなかった文字列だけ返す。** 文にするのは文言を持っている側（モジュール）の仕事で、
            // ここで日本語を組むと翻訳できなくなる。
            error = inside;
            return false;
        }

        selector = new DeviceSelector(address, text[..open].Trim() is { Length: > 0 } name ? name : null);
        return true;
    }

    /// <summary>ログに書く形。<b>英語</b>（アプリのログは言語に依らない）。</summary>
    public string Describe() => (Address, Name) switch
    {
        ({ } address, { } name) => $"{name} [{MonitorIdentity.FormatAddress(address)}]",
        ({ } address, null) => MonitorIdentity.FormatAddress(address),
        (null, { } name) => $"a device named like '{name}'",
        _ => "any heart rate monitor",
    };
}
