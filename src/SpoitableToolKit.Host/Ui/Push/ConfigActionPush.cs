using SpoitableToolKit.Host.Config.Models;

namespace SpoitableToolKit.Host.Ui.Push;

/// <summary>push（<see cref="ConfigActionPusher.Channel"/>）の形。<c>state</c> は設定の材料の <c>state</c> と同じもの。</summary>
internal sealed record ConfigActionPush(string Module, string Key, ConfigActionView State);
