namespace SpoitableToolKit.Modules.Hrs.Devices.Models;

/// <summary>スキャンで見つかった心拍計。</summary>
/// <param name="Address">BLE のアドレス（48 ビット）。名前と違って一意で、変わらない。</param>
internal readonly record struct MonitorIdentity(ulong Address, string Name)
{
    /// <summary>設定の候補に出す形。<c>名前 [アドレス]</c> で、両方を 1 つの文字列に持つ。</summary>
    public string ToSuggestion() => $"{Name} [{FormatAddress(Address)}]";

    public static string FormatAddress(ulong address) => string.Join(':',
        Enumerable.Range(0, 6).Select(i => ((address >> ((5 - i) * 8)) & 0xFF).ToString("X2")));

    /// <summary>
    /// <see cref="FormatAddress"/> の形（<c>AA:BB:CC:DD:EE:FF</c>）だけを読む。<b>設定の欄より厳しい</b>（<see cref="DeviceSelector.TryParse"/> は
    /// 16 進以外の字を飛ばして読む）— 画面から届いたアドレスを、そのまま繋ぐ相手の指定にするため。0 は読めないことにする（アドレスの無い相手）。
    /// </summary>
    public static bool TryParseAddress(string? text, out ulong address)
    {
        address = 0;
        var parts = text?.Split(':');
        if (parts is not { Length: 6 }) return false;

        foreach (var part in parts)
        {
            if (part.Length != 2 || !byte.TryParse(part, System.Globalization.NumberStyles.AllowHexSpecifier, null, out var octet)) return false;
            address = (address << 8) | octet;
        }

        return address != 0;
    }

    /// <summary>
    /// 同じ心拍計か。BLE は<b>アドレスだけ</b>で見る（名前は繋いだあとで名乗り直すことがある）。アドレスの無いもの（時計 = HDS の送り主）は名前で見る。
    /// </summary>
    public bool IsSame(MonitorIdentity other) =>
        Address != 0 ? Address == other.Address : other.Address == 0 && Name == other.Name;
}
