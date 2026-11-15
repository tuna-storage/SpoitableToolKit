using Greetings.Contract;
using SpoitableToolKit.Abstractions;

namespace GreeterUser;

public sealed class User : ISpoitableModule
{
    private IModuleHost? _host;

    public bool OnInit(IModuleHost host)
    {
        _host = host;
        return true;
    }

    /// <summary>ほかのモジュールの API を取ってよいのは、ここから。結果は自分の置き場に書く（試験がそれを読む）。</summary>
    public void OnAllModulesLoaded()
    {
        var greeter = _host!.ExternalModules.GetRequired<IGreeter>(IGreeter.ModuleIdentity);
        File.WriteAllText(Path.Combine(_host.DataDirectory, "greeting.txt"), greeter.Instance!.Greet("user"));
    }
}
