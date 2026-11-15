using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Ui;
using TestContract;

namespace ProviderModule;

/// <summary>
/// 契約を提供する側の見本。OnPostInit で登録する。
/// <para>
/// <b>画面まわりの interface（<see cref="IUiEndpoint"/>・<see cref="IContextMenuSource"/>）を実装しているが、<c>host.Ui</c> には何も登録しない。</b>
/// 実装しているだけでは本体が何も出さない（呼ばない）ことを、ここで見る。
/// </para>
/// </summary>
public sealed class ProviderModule : ISpoitableModule, IGreeter, IUiEndpoint, IContextMenuSource
{
    private IModuleHost? _host;

    public string Id => "provider";

    public string DisplayName => "Provider";

    public bool OnInit(IModuleHost host)
    {
        _host = host;
        return true;
    }

    public void OnPostInit() => _host!.ExternalModules.Register<IGreeter>(this, IGreeter.ModuleIdentity, this);

    public string Greet() => "hello from provider";

    public Task<string> HandleUiCallAsync(string method, string payload, CancellationToken cancellationToken = default) =>
        Task.FromResult("""{"provider":"should not be called"}""");

    public IReadOnlyList<ContextMenuItem> GetContextMenu(ContextMenuTarget target) =>
        [new ContextMenuItem { Id = "provider-item", LabelKey = "provider.menu.item" }];

    public Task InvokeContextMenuAsync(string id, ContextMenuTarget target, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
