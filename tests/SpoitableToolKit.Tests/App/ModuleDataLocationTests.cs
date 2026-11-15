using SpoitableToolKit.Host.App;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.App;

/// <summary><see cref="ModuleDataLocation"/>: 古い置き場には心拍の記録と各モジュールの設定がある。捨てない・取り違えない・移せなければ古い方を使い続ける。</summary>
public sealed class ModuleDataLocationTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "stk-tests", Guid.NewGuid().ToString("N"))).FullName;

    private readonly Logs _logs = new();

    private string Old => Path.Combine(_root, "plugin-data");

    private string Current => Path.Combine(_root, "module-data");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private void Write(string folder, string relative, string content)
    {
        var path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void AFirstStartGetsAnEmptyModuleData()
    {
        Assert.Equal(Current, ModuleDataLocation.Resolve(_root, _logs));

        Assert.True(Directory.Exists(Current));
        Assert.False(Directory.Exists(Old));
        Assert.Empty(_logs.All());
    }

    [Fact]
    public void AnOldPluginDataIsMovedWithEverythingInItOnce()
    {
        Write(Old, Path.Combine("hrs", "config.json"), """{ "Device": "Polar" }""");
        Write(Old, Path.Combine("hrs", "records.db"), "beats");

        Assert.Equal(Current, ModuleDataLocation.Resolve(_root, _logs));
        Assert.Equal(Current, ModuleDataLocation.Resolve(_root, _logs));

        Assert.False(Directory.Exists(Old));
        Assert.Equal("""{ "Device": "Polar" }""", File.ReadAllText(Path.Combine(Current, "hrs", "config.json")));
        Assert.Equal("beats", File.ReadAllText(Path.Combine(Current, "hrs", "records.db")));
        Assert.Equal([$"Moved the modules' data from {Old} to {Current}"], _logs.Infos);
    }

    /// <summary>新しい置き場が空（作られただけ・空のモジュールのフォルダがあるだけ）でも古い方を移す。空のほうを使うと、記録も設定も無いところから始まる。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnEmptyModuleDataMadeEarlierDoesNotHideTheOldOne(bool withAnEmptyModuleFolder)
    {
        Directory.CreateDirectory(withAnEmptyModuleFolder ? Path.Combine(Current, "hrs") : Current);
        Write(Old, Path.Combine("hrs", "config.json"), "kept");

        Assert.Equal(Current, ModuleDataLocation.Resolve(_root, _logs));

        Assert.False(Directory.Exists(Old));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(Current, "hrs", "config.json")));
    }

    /// <summary>両方に中身がある: どちらも動かさず、新しい方を使い、古い方が残っていると警告に書く（混ぜるとどちらの設定か分からなくなる）。</summary>
    [Fact]
    public void WhenBothHoldFilesNothingIsMovedAndTheWarningSaysSo()
    {
        Write(Old, Path.Combine("hrs", "config.json"), "old");
        Write(Current, Path.Combine("hrs", "config.json"), "new");

        Assert.Equal(Current, ModuleDataLocation.Resolve(_root, _logs));

        Assert.Equal("old", File.ReadAllText(Path.Combine(Old, "hrs", "config.json")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(Current, "hrs", "config.json")));
        Assert.Equal(
            [$"Both {Old} and {Current} have files; using the new one and leaving the old one as it is (move what is still needed by hand)"],
            _logs.Warnings);
    }

    /// <summary>移せない（中のファイルを誰かが開いている）: その回は古い置き場を使う。新しい空の置き場を作ると、次の起動で「両方ある」に見えて移らなくなる。</summary>
    [Fact]
    public void AFolderThatCannotBeMovedIsUsedWhereItIsThisTime()
    {
        Write(Old, Path.Combine("hrs", "records.db"), "beats");

        string used;
        using (new FileStream(Path.Combine(Old, "hrs", "records.db"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            used = ModuleDataLocation.Resolve(_root, _logs);
        }

        Assert.Equal(Old, used);
        Assert.False(Directory.Exists(Current));
        Assert.Equal("beats", File.ReadAllText(Path.Combine(Old, "hrs", "records.db")));
        Assert.StartsWith($"Could not move {Old} to {Current}; using the old one this time", Assert.Single(_logs.Warnings));

        Assert.Equal(Current, ModuleDataLocation.Resolve(_root, _logs));
        Assert.False(Directory.Exists(Old));
    }
}
