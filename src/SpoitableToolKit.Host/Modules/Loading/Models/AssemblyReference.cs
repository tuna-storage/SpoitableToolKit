namespace SpoitableToolKit.Host.Modules.Loading.Models;

/// <summary>dll が参照しているアセンブリ（名前と、作ったときの版）。</summary>
internal sealed record AssemblyReference(string Name, Version Version)
{
    public override string ToString() => $"{Name} {Version}";
}
