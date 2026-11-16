namespace SpoitableToolKit.Modules.Hrs.Panel;

/// <summary>時計のアプリに入れる宛先 1 つ。</summary>
/// <param name="Name">口の名前（Wi-Fi・イーサネットなど）。アドレスが 2 つ以上あるときに見分けるため。</param>
/// <param name="Endpoint"><c>IP:ポート</c>。コピーするのはこれ。</param>
internal sealed record HrsPanelHdsAddress(string Name, string Endpoint);
