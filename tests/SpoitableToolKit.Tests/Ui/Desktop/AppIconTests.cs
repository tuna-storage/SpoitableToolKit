using System.Runtime.InteropServices;
using SpoitableToolKit.Host.Ui.Desktop;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Desktop;

/// <summary>
/// exe と窓のアイコンが Assets/app-icon.ico そのものであること。
/// Windows はアイコンの無い exe にも既定の絵を返すので、取り出せるかではなく、中の絵を .ico と 1 バイトずつ突き合わせる。
/// </summary>
public sealed class AppIconTests
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpoitableToolKit.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("repo の一番上が見つからない");
    }

    private static byte[] Ico() => File.ReadAllBytes(Path.Combine(RepoRoot(), "src", "SpoitableToolKit.Host", "Assets", "app-icon.ico"));

    /// <summary>大きさごとの絵。256 は幅 0 と書く決まり。</summary>
    private static Dictionary<int, byte[]> ImagesIn(byte[] ico)
    {
        Dictionary<int, byte[]> images = [];
        for (var i = 0; i < BitConverter.ToUInt16(ico, 4); i++)
        {
            var entry = 6 + 16 * i;
            var offset = BitConverter.ToInt32(ico, entry + 12);
            images.Add(ico[entry] == 0 ? 256 : ico[entry], ico[offset..(offset + BitConverter.ToInt32(ico, entry + 8))]);
        }

        return images;
    }

    [Fact]
    public void TheIcoHasEverySize()
    {
        var sizes = ImagesIn(Ico()).Keys.Order().ToArray();
        Assert.Equal(Sizes, sizes);
    }

    /// <summary>試験の出力に写っている Host の exe（Host を参照しているので build で写る）を、走らせずに資源だけ読む。</summary>
    [Fact]
    public void TheExeCarriesTheIco()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "SpoitableToolKit.Host.exe");
        Assert.True(File.Exists(exe), $"{exe} が無い");

        var module = LoadLibraryEx(exe, IntPtr.Zero, LoadLibraryAsDatafile | LoadLibraryAsImageResource);
        Assert.True(module != IntPtr.Zero, $"{exe} を開けない（{Marshal.GetLastWin32Error()}）");
        try
        {
            List<string> groups = [];
            EnumResourceNames(module, RtGroupIcon, (_, _, name, _) =>
            {
                groups.Add(name >>> 16 == 0 ? $"#{name}" : Marshal.PtrToStringUni(name)!);
                return true;
            }, IntPtr.Zero);
            Assert.True(groups.Count == 1, $"exe のアイコンの組が {groups.Count} 個（csproj の ApplicationIcon が効いていない）");

            // GRPICONDIR: 頭 6 バイト、続いて 1 つ 14 バイト（末尾 2 バイトが RT_ICON の番号）。
            var directory = Resource(module, groups[0], RtGroupIcon);
            Dictionary<int, byte[]> found = [];
            for (var i = 0; i < BitConverter.ToUInt16(directory, 4); i++)
            {
                var entry = 6 + 14 * i;
                found.Add(directory[entry] == 0 ? 256 : directory[entry], Resource(module, $"#{BitConverter.ToUInt16(directory, entry + 12)}", RtIcon));
            }

            var expected = ImagesIn(Ico());
            var sizes = found.Keys.Order().ToArray();
            Assert.Equal(expected.Keys.Order().ToArray(), sizes);
            Assert.All(expected, pair => Assert.True(pair.Value.AsSpan().SequenceEqual(found[pair.Key]),
                $"exe の {pair.Key} の絵が .ico と違う（.ico を作り直したあと build し直していない）"));
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    /// <summary>窓は dll に埋めた方を使う（WinForms の窓は exe のアイコンを見ない）。</summary>
    [Fact]
    public void TheWindowsUseTheSameIco()
    {
        using (var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(AppIcon.ResourceName))
        {
            Assert.NotNull(stream);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            Assert.True(copy.ToArray().AsSpan().SequenceEqual(Ico()), "dll に埋めた .ico が Assets/app-icon.ico と違う");
        }

        // タイトルバーの大きさ（WinForms が窓の Icon から選び直すのと同じ道）で、真ん中が白いハート・左の縁が accent の地。
        using var small = new Icon(AppIcon.Window, 16, 16);
        using var bitmap = small.ToBitmap();
        Assert.Equal(16, bitmap.Width);

        var middle = bitmap.GetPixel(8, 8);
        var edge = bitmap.GetPixel(2, 8);
        Assert.True(middle is { A: 255, R: > 230, G: > 230, B: > 230 }, $"真ん中が {middle}");
        Assert.True(edge.A == 255 && Math.Abs(edge.R - 0x25) < 12 && Math.Abs(edge.G - 0x63) < 12 && Math.Abs(edge.B - 0xEB) < 12, $"左の縁が {edge}");
    }

    private static byte[] Resource(IntPtr module, string name, IntPtr type)
    {
        var info = FindResource(module, name, type);
        Assert.True(info != IntPtr.Zero, $"資源 {name} が無い");

        var bytes = new byte[SizeofResource(module, info)];
        Marshal.Copy(LockResource(LoadResource(module, info)), bytes, 0, bytes.Length);
        return bytes;
    }

    private const uint LoadLibraryAsDatafile = 0x2;
    private const uint LoadLibraryAsImageResource = 0x20;
    private static readonly IntPtr RtIcon = 3;
    private static readonly IntPtr RtGroupIcon = 14;

    private delegate bool EnumResourceNameCallback(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LoadLibraryExW")]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumResourceNamesW")]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumResourceNameCallback callback, IntPtr parameter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindResourceW")]
    private static extern IntPtr FindResource(IntPtr module, string name, IntPtr type);

    [DllImport("kernel32.dll")]
    private static extern uint SizeofResource(IntPtr module, IntPtr info);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr info);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LockResource(IntPtr data);
}
