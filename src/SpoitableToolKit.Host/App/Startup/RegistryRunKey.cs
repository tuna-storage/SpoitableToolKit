using Microsoft.Win32;

namespace SpoitableToolKit.Host.App.Startup;

/// <summary>
/// HKCU の <c>Software\Microsoft\Windows\CurrentVersion\Run</c> の値 1 つ。利用者ごとの登録なので、管理者は要らない。
/// Windows の「スタートアップ アプリ」の一覧にも、この値の名前で並ぶ（そこで切ると、Windows が別の場所に切ったことを覚える。この値は消えない）。
/// </summary>
internal sealed class RegistryRunKey(string valueName) : IRunKey
{
    public const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string Where => $@"HKCU\{KeyPath}\{valueName}";

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(valueName) as string;
    }

    public void Write(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(valueName, command, RegistryValueKind.String);
    }

    public void Remove()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
