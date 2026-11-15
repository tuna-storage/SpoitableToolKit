namespace SpoitableToolKit.Host.Modules.Build;

/// <summary>見つけた .NET SDK（<paramref name="Dotnet"/> は <c>dotnet.exe</c>、<paramref name="Version"/> はその中でいちばん新しい使える SDK）。</summary>
internal sealed record DotnetSdk(string Dotnet, Version Version);
