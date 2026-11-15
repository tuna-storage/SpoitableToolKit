namespace SpoitableToolKit.Host.Modules.Loading.Models;

/// <summary>
/// 読めない理由。<paramref name="From"/> は参照している dll（入口なら null、共有の dll ならその名前）。
/// <paramref name="Available"/> は載っている版（<see cref="DependencyProblemKind.TooOld"/> と <see cref="DependencyProblemKind.HostTooOld"/>。
/// <see cref="DependencyProblemKind.Missing"/> では、本体が古い版だけを持っているときのその版）、
/// <paramref name="Provider"/> は持ってきたモジュール（<see cref="DependencyProblemKind.TooOld"/> だけ）。
/// </summary>
internal sealed record DependencyProblem(
    DependencyProblemKind Kind, AssemblyReference Needed, string? From, Version? Available = null, string? Provider = null)
{
    public override string ToString()
    {
        var who = From is null ? string.Empty : $" (needed by the shared {From})";
        return Kind switch
        {
            DependencyProblemKind.TooOld =>
                $"{Needed} is needed{who}, but the shared copy is {Available} (brought by {Provider}); update {Provider}",
            DependencyProblemKind.HostTooOld =>
                $"{Needed} is needed{who}, but this app has {Available}; update the app (the module was built for a newer version of it)",
            DependencyProblemKind.SdkTooOld =>
                $"{Needed} is referenced{who}, but this app loads modules built with SDK {Available?.ToString(3)} or newer; " +
                "the module's developer has to build it again with a newer SDK",
            _ when Available is not null =>
                $"{Needed} was not found{who}: the host only has {Available}, the module's folder has no copy, and no running module shares it; " +
                $"the module has to bring its own {Needed.Name}.dll",
            _ => $"{Needed} was not found{who}: not in the host, not in the module's folder, and no running module shares it",
        };
    }
}
