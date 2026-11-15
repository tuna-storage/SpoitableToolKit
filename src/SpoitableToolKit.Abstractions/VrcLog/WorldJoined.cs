namespace SpoitableToolKit.Abstractions.VrcLog;

/// <summary>ワールドへの入室。<c>Entering Room:</c> と <c>Joining wrld_...</c> の 2 行を合流させたもの。</summary>
public sealed record WorldJoined : VrcLogEventParams
{
    public required string WorldId { get; init; }

    /// <summary>インスタンス名（<c>59273~friends(usr_...)~region(jp)</c> の先頭部分）。</summary>
    public required string InstanceName { get; init; }

    /// <summary>直前の <c>Entering Room:</c> 行から拾った人間可読なワールド名。</summary>
    public string? WorldName { get; init; }

    /// <summary><c>public</c> / <c>friends</c> / <c>private</c> / <c>hidden</c> / <c>group</c> など。判別できなければ null。</summary>
    public string? AccessType { get; init; }

    /// <summary>インスタンスのオーナー（<c>usr_...</c> / <c>grp_...</c>）。public インスタンスでは付かない。</summary>
    public string? OwnerId { get; init; }

    /// <summary><c>region(jp)</c> の <c>jp</c> 部分。</summary>
    public string? Region { get; init; }

    /// <summary>インスタンス文字列の生の形。パースを取りこぼしても原文が残るように保持する。</summary>
    public required string RawInstance { get; init; }
}
