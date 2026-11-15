using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Config;

namespace BrokenConfigModule;

/// <summary>
/// 設定を渡す所で投げる見本。本体はモジュールを並べたあと、OnInit の前に設定を読むので、
/// 「並べたのに OnInit まで行けなかった」形になる。
/// </summary>
public sealed class BrokenConfigModule : ISpoitableModule
{
    public string Id => "brokenconfig";

    public string DisplayName => "Broken config";

    public IModuleConfig? Config => throw new InvalidOperationException("the config could not be built");

    public bool OnInit(IModuleHost host) => true;
}
