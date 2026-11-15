using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Host.Settings;

namespace SpoitableToolKit.Host.Events;

/// <summary>
/// アプリ自身を、listener と口（<see cref="EventHub"/>）の持ち主として表すもの。Id は <see cref="HostConfig.Id"/>（<c>host</c>）。
/// Host の中の受け手（ログを画面へ流す・インスタンスを追う など）もモジュールと同じ口・同じ確かめを通して足すために作った
/// （owner を取らない Host だけの口を別に作ると、登録の道が 2 本になって片方だけ試験が抜ける）。
/// モジュールの一覧には載らず、畳まれることもない（足した listener が外れるのはアプリが終わるとき）。
/// モジュールは <c>host</c> を名乗れない（<see cref="SpoitableToolKit.Host.Modules.Packaging.ModuleManifest.IsReservedId"/>）ので、Id はぶつからない。
/// </summary>
internal sealed class HostOwner : ISpoitableModule
{
    public static HostOwner Instance { get; } = new();

    private HostOwner()
    {
    }

    public string Id => HostConfig.Id;

    public string DisplayName => "SpoitableToolKit";

    public bool OnInit(IModuleHost host) =>
        throw new NotSupportedException("HostOwner stands for the app itself as an owner of listeners and event hubs; it is never initialised as a module.");
}
