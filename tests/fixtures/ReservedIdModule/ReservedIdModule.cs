using SpoitableToolKit.Abstractions;

namespace ReservedIdModule;

/// <summary>
/// 本体が自分のために使っている名前（<c>host</c>）を Id にしたモジュール。本体は読み込まない。
/// </summary>
public sealed class ReservedIdModule : ISpoitableModule
{
    public string Id => "host";

    public string DisplayName => "Pretends to be the app";

    public bool OnInit(IModuleHost host) => true;
}
