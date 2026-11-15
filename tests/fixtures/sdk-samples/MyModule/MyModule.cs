using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions;

namespace MyModule;

/// <summary>
/// いちばん小さいモジュール。Id と名前は csproj に書いたもの（build が作る <c>ModuleInfo</c>）を返す（クラスには書かない — 書くと STK040）。
/// <c>web/index.html</c> をパネルとして登録する（登録しないとサイドバーに出ない — 忘れると STK041）。
/// ログは <see cref="IModuleHost.Logger"/> に書く（アプリのログの画面に、このモジュールの Id で出る）。
/// </summary>
public sealed class MyModule : ISpoitableModule
{
    public bool OnInit(IModuleHost host)
    {
        host.Ui.RegisterPanel("index.html");
        host.Logger.LogInformation("The sample module is running");
        return true;
    }
}
