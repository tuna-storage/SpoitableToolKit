namespace SpoitableToolKit.Modules.Hrs.Devices.Ble;

/// <summary>
/// Heart Rate Measurement（BLE の <c>0x2A37</c>）の読み方。
/// <para>
/// 先頭 1 バイトが flags で、<b>bit0 が「心拍値が 16 ビットか」</b>。
/// 腕時計型は 8 ビット、胸ベルトは 16 ビットで送ってくることがあり、
/// 決め打ちにすると片方で桁が壊れる。
/// </para>
/// </summary>
internal static class HrsMeasurement
{
    /// <summary>読めなければ <c>null</c>。短いパケットは実機でも来るので、例外にしない。</summary>
    public static int? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2) return null;

        var is16Bit = (data[0] & 0x01) != 0;

        if (!is16Bit) return data[1];

        return data.Length < 3 ? null : data[1] | (data[2] << 8);
    }

    /// <summary>Polar の会社 ID。広告の製造元データを見分けるのに使う。</summary>
    public const ushort PolarCompanyId = 0x006B;

    /// <summary>
    /// Polar が広告に載せてくる心拍。<b>末尾 1 バイトが心拍</b>。
    /// <para>
    /// 仕様が公開されているわけではないので、<b>値が人の心拍として有り得る範囲かどうかで
    /// 「読めた」を判断する</b>（10 バイト未満は別の用途の広告）。外れた値を流すと、
    /// アバターの心拍表示が跳ねる。
    /// </para>
    /// </summary>
    public static int? ParsePolarBroadcast(ReadOnlySpan<byte> data)
    {
        if (data.Length < 10) return null;

        var heartRate = data[^1];

        return heartRate is >= 30 and <= 240 ? heartRate : null;
    }
}
