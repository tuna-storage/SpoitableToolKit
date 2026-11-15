namespace SpoitableToolKit.Sdk.Analyzers;

/// <summary>アナライザーが探す SDK の型の名前（Abstractions の型の完全名。名前を変えたら、ここも変える — 試験が見張る）。</summary>
internal static class KnownTypes
{
    public const string SpoitableModule = "SpoitableToolKit.Abstractions.ISpoitableModule";

    public const string ModuleUi = "SpoitableToolKit.Abstractions.Ui.IModuleUi";
}
