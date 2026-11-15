using System.Reflection;

namespace SpoitableToolKit.Host.App;

internal static class AppVersion
{
    public static string Text { get; } =
        Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
