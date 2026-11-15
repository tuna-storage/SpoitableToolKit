namespace SpoitableToolKit.Tests.Sdk;

/// <summary>いちばん小さい見本（<c>MyModule</c>）を build した置き場。最初の build（restore あり・zip まで）は 1 回だけで、試験はその結果を読む。</summary>
public sealed class SdkWorkspace : SdkSandbox
{
    /// <summary>JSON・C#・MSBuild で意味を持つ字を入れてある。</summary>
    public const string ModuleName = "心拍; \"x\" \\ 50% it's (a) *? @(b)";

    public SdkBuild Packed { get; private set; } = null!;

    /// <summary><c>TargetFrameworks</c>（複数形）で書いた写しを zip にしようとした結果（止まるはず）。</summary>
    public SdkBuild MultiTargeted { get; private set; } = null!;

    /// <summary>listener を、型を書かずに足す・外す写しを build した結果（CS0411 で止まるはず）。</summary>
    public SdkBuild NoListenerType { get; private set; } = null!;

    /// <summary><see cref="NoListenerType"/> に足したソース（<c>ListenerUses.cs</c>）。型を書いた行と、書いていない行がある。</summary>
    public static readonly string[] ListenerSource =
    [
        "using SpoitableToolKit.Abstractions;",
        "using SpoitableToolKit.Abstractions.Events;",
        "",
        "namespace MyModule;",
        "",
        "public interface IMyListener : IEventListener",
        "{",
        "    void OnSomething() { }",
        "}",
        "",
        "public static class ListenerUses",
        "{",
        "    public static void Typed(IEventSource source, ISpoitableModule owner, IMyListener listener)",
        "    {",
        "        source.AddListener<IMyListener>(owner, listener);",
        "        source.RemoveListener<IMyListener>(owner, listener);",
        "    }",
        "",
        "    public static void Untyped(IEventSource source, ISpoitableModule owner, IMyListener listener)",
        "    {",
        "        source.AddListener(owner, listener);",
        "        source.RemoveListener(owner, listener);",
        "    }",
        "}",
    ];

    public static int[] UntypedLines =>
        [.. ListenerSource.Select((line, index) => (line, index)).Where(x => x.line.Contains("Listener(owner")).Select(x => x.index + 1)];

    /// <summary>モジュールのクラスに Id と DisplayName を手で書いた写しを build した結果（STK040 で止まるはず）。</summary>
    public SdkBuild HandwrittenIdentity { get; private set; } = null!;

    /// <summary><see cref="HandwrittenIdentity"/> のモジュールのクラス（<c>MyModule.cs</c> を差し替える）。Id は 8 行目、DisplayName は 10 行目。</summary>
    public static readonly string[] HandwrittenIdentitySource =
    [
        "using SpoitableToolKit.Abstractions;",
        "",
        "namespace MyModule;",
        "",
        "public sealed class MyModule : ISpoitableModule",
        "{",
        "    public bool OnInit(IModuleHost host) { host.Ui.RegisterPanel(\"index.html\"); return true; }",
        "    public string Id => \"mymodule\";",
        "",
        "    public string DisplayName => \"My module\";",
        "}",
    ];

    /// <summary><c>web/index.html</c> があるのにパネルを登録しない写しを build した結果（通るが STK041 の警告が出るはず）。</summary>
    public SdkBuild UnregisteredPanel { get; private set; } = null!;

    public IReadOnlyList<string> OutputFiles { get; private set; } = [];

    public byte[] Zip { get; private set; } = [];

    public string Output => OutputOf("MyModule");

    public string ZipPath => ZipOf("MyModule", "mymodule", "0.3.0");

    public SdkWorkspace() => Setup(() =>
    {
        const string pack = "-t:SpoitablePackModule";

        Variant("MyModule", "MultiTargeted",
            "<TargetFramework>net10.0</TargetFramework>",
            "<TargetFrameworks>net10.0</TargetFrameworks>");

        CopyDirectory(Path.Combine(Root, "MyModule"), Path.Combine(Root, "NoListenerType"));
        File.WriteAllLines(Path.Combine(Root, "NoListenerType", "ListenerUses.cs"), ListenerSource);

        CopyDirectory(Path.Combine(Root, "MyModule"), Path.Combine(Root, "HandwrittenIdentity"));
        File.WriteAllLines(Path.Combine(Root, "HandwrittenIdentity", "MyModule.cs"), HandwrittenIdentitySource);

        CopyDirectory(Path.Combine(Root, "MyModule"), Path.Combine(Root, "UnregisteredPanel"));
        File.WriteAllLines(Path.Combine(Root, "UnregisteredPanel", "MyModule.cs"),
            ["using SpoitableToolKit.Abstractions;", "namespace MyModule;", "public sealed class MyModule : ISpoitableModule { public bool OnInit(IModuleHost host) => true; }"]);

        // 写しは見本と関わらないので、並べて走らせる。
        var multiTargeted = Task.Run(() => Build("MultiTargeted", restore: true, pack));
        var noListenerType = Task.Run(() => Build("NoListenerType", restore: true));
        var handwritten = Task.Run(() => Build("HandwrittenIdentity", restore: true));
        var unregistered = Task.Run(() => Build("UnregisteredPanel", restore: true));
        Packed = Build("MyModule", restore: true, pack);
        OutputFiles = FilesUnder(Output);
        Zip = File.Exists(ZipPath) ? File.ReadAllBytes(ZipPath) : [];
        MultiTargeted = multiTargeted.GetAwaiter().GetResult();
        NoListenerType = noListenerType.GetAwaiter().GetResult();
        HandwrittenIdentity = handwritten.GetAwaiter().GetResult();
        UnregisteredPanel = unregistered.GetAwaiter().GetResult();
    });
}
