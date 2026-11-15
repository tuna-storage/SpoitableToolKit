using Greetings.Contract;
using SpoitableToolKit.Abstractions;

namespace GreeterModule;

public sealed class Greeter : ISpoitableModule, IGreeter
{
    private IModuleHost? _host;

    public bool OnInit(IModuleHost host)
    {
        _host = host;
        return true;
    }

    public void OnPostInit() => _host!.ExternalModules.Register<IGreeter>(this, IGreeter.ModuleIdentity, this);

    public string Greet(string name) => $"hello, {name}";
}
