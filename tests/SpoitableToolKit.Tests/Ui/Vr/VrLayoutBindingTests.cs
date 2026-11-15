using System.Text.Json;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Vr;

/// <summary>
/// Layout Mode の間は入力をすべてこちらで預かる。SteamVR は優先度の高い action set が割り当てた入力元だけを下のアプリから外すので、
/// 割り当てていない入力元は素通りする。止められない入力元（system）と、骨格・姿勢・振動・指の曲げは数えない。
/// </summary>
public sealed class VrLayoutBindingTests
{
    private static readonly DirectoryInfo Root = FindRoot();

    private static DirectoryInfo FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpoitableToolKit.slnx")))
            directory = directory.Parent;

        return directory ?? throw new InvalidOperationException("リポジトリの根が見つからない");
    }

    public static TheoryData<string, string?, string, string> Held()
    {
        var data = new TheoryData<string, string?, string, string>();

        void Add(string kind, string? hand, string source, params string[] parts)
        {
            foreach (var part in parts) data.Add(kind, hand, source, part);
        }

        string[] stick = ["position", "click", "touch"];

        Add("oculus_touch", null, "joystick", stick);
        Add("oculus_touch", "left", "x", "click", "touch");
        Add("oculus_touch", "left", "y", "click", "touch");
        Add("oculus_touch", "right", "a", "click", "touch");
        Add("oculus_touch", "right", "b", "click", "touch");
        Add("oculus_touch", null, "thumbrest", "touch");
        Add("oculus_touch", null, "trigger", "click");
        Add("oculus_touch", null, "grip", "click");

        Add("knuckles", null, "thumbstick", stick);
        Add("knuckles", null, "trackpad", stick);
        Add("knuckles", null, "a", "click", "touch");
        Add("knuckles", null, "b", "click", "touch");
        Add("knuckles", null, "trigger", "click");
        Add("knuckles", null, "grip", "grab");

        Add("vive_controller", null, "trackpad", stick);
        Add("vive_controller", null, "application_menu", "click");
        Add("vive_controller", null, "trigger", "click");
        Add("vive_controller", null, "grip", "click");

        return data;
    }

    [Theory]
    [MemberData(nameof(Held))]
    public void EveryInputIsHeldDuringLayoutMode(string kind, string? hand, string source, string part)
    {
        var bound = Bound(kind);

        string[] sides = hand is null ? ["left", "right"] : [hand];

        foreach (var side in sides)
        {
            var path = $"/user/hand/{side}/input/{source}";
            Assert.True(bound.Contains((path, part)), $"{kind}: {path} の {part} が預かられていない（Layout Mode の間も VRChat に届く）");
        }
    }

    /// <summary>書き間違えた action に割り当てると、SteamVR は黙ってその行を捨てる（入力元が素通りに戻る）。</summary>
    [Theory]
    [InlineData("oculus_touch")]
    [InlineData("knuckles")]
    [InlineData("vive_controller")]
    public void LayoutBindingsOnlyFeedTheLayoutActions(string kind)
    {
        using var bindings = JsonDocument.Parse(File.ReadAllText(Path.Combine(VrDirectory, $"bindings_{kind}.json")));
        var outputs = bindings.RootElement.GetProperty("bindings").GetProperty("/actions/layout").GetProperty("sources").EnumerateArray()
            .SelectMany(source => source.GetProperty("inputs").EnumerateObject())
            .Select(input => input.Value.GetProperty("output").GetString()!)
            .Distinct()
            .Order()
            .ToArray();

        Assert.Equal(["/actions/layout/in/block", "/actions/layout/in/block_pos"], outputs);
        Assert.Subset(ManifestActions(), outputs.ToHashSet());
    }

    private static string VrDirectory => Path.Combine(Root.FullName, "src", "SpoitableToolKit.Host", "vr");

    private static HashSet<(string Path, string Part)> Bound(string kind)
    {
        using var bindings = JsonDocument.Parse(File.ReadAllText(Path.Combine(VrDirectory, $"bindings_{kind}.json")));
        var sets = bindings.RootElement.GetProperty("bindings");
        var bound = new HashSet<(string, string)>();

        foreach (var set in new[] { "/actions/pointer", "/actions/layout" })
        {
            foreach (var source in sets.GetProperty(set).GetProperty("sources").EnumerateArray())
            {
                var path = source.GetProperty("path").GetString()!;
                foreach (var input in source.GetProperty("inputs").EnumerateObject()) bound.Add((path, input.Name));
            }
        }

        return bound;
    }

    private static HashSet<string> ManifestActions()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(VrDirectory, "actions.json")));
        return manifest.RootElement.GetProperty("actions").EnumerateArray().Select(a => a.GetProperty("name").GetString()!).ToHashSet();
    }
}
