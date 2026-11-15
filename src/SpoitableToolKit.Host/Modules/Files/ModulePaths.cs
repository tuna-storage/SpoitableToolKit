using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SpoitableToolKit.Host.Modules.Files;

/// <summary>
/// モジュールの置き場の道の比べ方（中にあるか・リンクか・同じフォルダか）。<b>外す・入れる・片付けるが、どれも同じ決まりで見る</b>
/// （別々に書くと、どこかで <c>..</c> やリンクの扱いがずれて、置き場の外を消す）。
/// </summary>
internal static class ModulePaths
{
    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary><paramref name="path"/> が <paramref name="parent"/> の<b>中</b>にあるか（同じ場所は含めない。<c>..</c> は解いてから比べる）。</summary>
    public static bool IsUnder(string path, string parent)
    {
        var full = Normalize(path);
        var root = Normalize(parent) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && full.Length > root.Length;
    }

    /// <summary>
    /// <paramref name="path"/> がリンク（junction・シンボリックリンク）か。<b>無ければ false</b>。
    /// 片付けの作業場（<c>.trash</c> など）がリンクだと、その先のフォルダを消してしまう。
    /// </summary>
    public static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 2 つの道が<b>同じフォルダ</b>を指すか。字が同じなら同じ。違っても、開いてみて同じ物（ボリュームの番号とファイルの番号）なら同じ
    /// （junction・シンボリックリンク・subst のドライブ。<c>Path.GetFullPath</c> はどれも解かない）。
    /// 開けなければ字の比較だけになる。ネットワーク越しの別名（<c>\\localhost\D$</c> など）で番号が揃うかは確かめていない。
    /// </summary>
    public static bool SameDirectory(string first, string second)
    {
        if (string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase)) return true;
        return Identity(first) is { } a && Identity(second) is { } b && a == b;
    }

    private static (uint Volume, ulong Index)? Identity(string directory)
    {
        // 中身は読まない（access 0）。FILE_FLAG_BACKUP_SEMANTICS が無いとフォルダは開けない。リンクは先まで辿る（開くのは指している先）。
        using var handle = CreateFileW(Normalize(directory), 0, FileShare.ReadWrite | FileShare.Delete, 0, FileMode.Open, 0x02000000, 0);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var information)) return null;
        return (information.VolumeSerialNumber, ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string name, uint access, FileShare share, nint security, FileMode mode, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
