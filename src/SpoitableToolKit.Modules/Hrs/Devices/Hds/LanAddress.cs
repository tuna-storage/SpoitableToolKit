using System.Net;

namespace SpoitableToolKit.Modules.Hrs.Devices.Hds;

/// <summary>時計から届きそうな、この PC の LAN の IPv4 アドレス 1 つと、その口の名前。</summary>
internal readonly record struct LanAddress(string Name, IPAddress Ip);
