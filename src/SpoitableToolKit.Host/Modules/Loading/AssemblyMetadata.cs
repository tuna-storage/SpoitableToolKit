using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using AssemblyReference = SpoitableToolKit.Host.Modules.Loading.Models.AssemblyReference;

namespace SpoitableToolKit.Host.Modules.Loading;

/// <summary>dll を<b>読み込まずに</b>中身（名前・版・参照）を読む。</summary>
internal static class AssemblyMetadata
{
    /// <summary>.NET の dll でなければ（ネイティブの dll など）null。</summary>
    public static (string Name, Version Version, IReadOnlyList<AssemblyReference> References)? Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return null;

            var reader = pe.GetMetadataReader();
            if (!reader.IsAssembly) return null;

            var definition = reader.GetAssemblyDefinition();
            var references = reader.AssemblyReferences
                .Select(handle => reader.GetAssemblyReference(handle))
                .Select(r => new AssemblyReference(reader.GetString(r.Name), r.Version))
                .ToArray();

            return (reader.GetString(definition.Name), definition.Version, references);
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            // .NET の dll でない・書きかけ・開けない。どれも「読めない」として扱う（1 つのせいで全部を止めない）。
            return null;
        }
    }

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
