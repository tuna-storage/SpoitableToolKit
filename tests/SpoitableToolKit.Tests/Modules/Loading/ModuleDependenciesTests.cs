using SpoitableToolKit.Host.Modules.Loading;
using SpoitableToolKit.Host.Modules.Loading.Models;
using Xunit;

namespace SpoitableToolKit.Tests.Modules.Loading;

/// <summary>
/// 読み込む前に、どのモジュールが動けるかと、どの共有の dll を載せるかを決める決まり（<see cref="ModuleDependencies.Resolve"/>）。
/// <para>
/// <b>dll を読み込まない純粋な関数</b>なので、版の違う同じ名前の dll を何通りでも試せる
/// （既定のコンテキストはプロセスで 1 つで、一度載せた名前は外せない。通しの試験では版の選び分けを確かめられない）。
/// </para>
/// </summary>
public sealed class ModuleDependenciesTests
{
    private static readonly Version V1 = new(2026, 11, 1);
    private static readonly Version V2 = new(2026, 11, 2);
    private static readonly Version V3 = new(2026, 11, 3);

    private static AssemblyReference Ref(string name, Version version) => new(name, version);

    private static SharedFile Shared(string owner, string name, Version version, string sha = "AA", params AssemblyReference[] references) =>
        new(owner, $@"C:\modules\{owner}\shared\{name}.dll", name, version, sha, references);

    private static ModuleUnit Module(string id, AssemblyReference[]? uses = null, params SharedFile[] shared) =>
        new(id, uses ?? [], shared);

    /// <summary>本体は .NET だけを持っている。モジュールのフォルダには何も無い。</summary>
    private static DependencyResolution Resolve(params ModuleUnit[] units) =>
        Resolve(units, own: (_, _) => false);

    private static DependencyResolution Resolve(ModuleUnit[] units, Func<string, AssemblyReference, bool> own) =>
        ModuleDependencies.Resolve(units, reference => reference.Name.StartsWith("System.", StringComparison.Ordinal), own,
            name => name.StartsWith("System.", StringComparison.Ordinal) ? new Version(10, 0) : null, NoMinimum);

    /// <summary>古さでは断らない（下限の試験だけが下限を渡す）。</summary>
    private static readonly Version NoMinimum = new(0, 0, 0, 0);

    private const string Api = ModuleDependencies.AppApi;

    [Fact]
    public void TheAppsApiIsTheAssemblyThatHoldsTheModuleInterface() =>
        Assert.Equal(ModuleDependencies.AppApi, typeof(SpoitableToolKit.Abstractions.ISpoitableModule).Assembly.GetName().Name);

    /// <summary>本体は .NET と、<paramref name="api"/> の版の API（<c>SpoitableToolKit.Abstractions</c>）を持っている。</summary>
    private static DependencyResolution ResolveInAnAppWithApi(Version api, params ModuleUnit[] units) =>
        ResolveInAnAppWithApi(api, NoMinimum, units);

    private static DependencyResolution ResolveInAnAppWithApi(Version api, Version minimum, params ModuleUnit[] units) =>
        ModuleDependencies.Resolve(units,
            reference => reference.Name.StartsWith("System.", StringComparison.Ordinal) || (reference.Name == Api && reference.Version <= api),
            (_, _) => false,
            name => name == Api ? api : null,
            minimum);

    /// <summary>
    /// この本体が読めるより古い SDK で作ったものは、本体が新しい版で出せても読まない（消えた・変わった型で落ちる）。理由は「作り直してもらう」。
    /// 下限と同じ版・新しい版で作ったものは動かす。共有の dll が古い API を参照していても、持ち主ごと止める。
    /// </summary>
    [Fact]
    public void AModuleBuiltWithAnOlderSdkThanTheAppCanLoadIsNotLoaded()
    {
        var result = ResolveInAnAppWithApi(V3, minimum: V2,
            Module("older", [Ref(Api, V1)]),
            Module("minimum", [Ref(Api, V2)]),
            Module("newest", [Ref(Api, V3)]),
            Module("shares", shared: Shared("shares", "S.Shared", V1, "AA", Ref(Api, V1))));

        Assert.Equal(new DependencyProblem(DependencyProblemKind.SdkTooOld, Ref(Api, V1), From: null, Available: V2), result.Failures["older"]);
        Assert.Equal(new DependencyProblem(DependencyProblemKind.SdkTooOld, Ref(Api, V1), From: "S.Shared", Available: V2), result.Failures["shares"]);
        Assert.Equal(["older", "shares"], result.Failures.Keys.Order());
        Assert.Equal(
            "SpoitableToolKit.Abstractions 2026.11.1 is referenced, but this app loads modules built with SDK 2026.11.2 or newer; " +
            "the module's developer has to build it again with a newer SDK",
            result.Failures["older"].ToString());
    }

    /// <summary>
    /// 本体より新しい SDK で作ったモジュールは読まない。理由は「見つからない」ではなく「アプリが古い」（直し方が違う: アプリの更新）。
    /// 同じ版・古い版で作ったものはそのまま動かす。
    /// </summary>
    [Fact]
    public void AModuleBuiltForANewerAppThanThisOneIsToldToUpdateTheApp()
    {
        var result = ResolveInAnAppWithApi(V2,
            Module("newer", [Ref(Api, V3)]),
            Module("same", [Ref(Api, V2)]),
            Module("older", [Ref(Api, V1)]));

        var problem = Assert.Single(result.Failures).Value;
        Assert.Equal(new DependencyProblem(DependencyProblemKind.HostTooOld, Ref(Api, V3), From: null, Available: V2), problem);
        Assert.Equal("newer", Assert.Single(result.Failures).Key);
    }

    /// <summary>共有の dll が本体より新しい API を参照しているときも同じ理由。どの dll が要るのかも出す。</summary>
    [Fact]
    public void ASharedDllBuiltForANewerAppStopsItsOwnerWithTheSameReason()
    {
        var result = ResolveInAnAppWithApi(V1,
            Module("a", shared: Shared("a", "A.Shared", V1, "AA", Ref(Api, V2))));

        Assert.Equal(new DependencyProblem(DependencyProblemKind.HostTooOld, Ref(Api, V2), From: "A.Shared", Available: V1), result.Failures["a"]);
        Assert.Empty(result.Shared);
    }

    /// <summary>
    /// 本体の API でない dll を、本体が古い版でだけ持っているとき: 「アプリを更新して」とは言わない（更新しても、その dll が新しくなるとは限らない）。
    /// 「見つからない」のまま、本体が持っている版と、モジュールが自分で持ってくることを言う。
    /// </summary>
    [Fact]
    public void AnOtherDllTheAppHasOnlyInAnOlderVersionIsForTheModuleToBring()
    {
        var result = ModuleDependencies.Resolve(
            [Module("b", [Ref("Some.Library", V3)])],
            reference => reference.Name == "Some.Library" && reference.Version <= V1,
            (_, _) => false,
            name => name == "Some.Library" ? V1 : null,
            NoMinimum);

        var problem = result.Failures["b"];
        Assert.Equal(new DependencyProblem(DependencyProblemKind.Missing, Ref("Some.Library", V3), From: null, Available: V1), problem);
        Assert.Equal(
            "Some.Library 2026.11.3 was not found: the host only has 2026.11.1, the module's folder has no copy, and no running module shares it; " +
            "the module has to bring its own Some.Library.dll",
            problem.ToString());
    }

    /// <summary>
    /// 本体が同じ版を持っているのに読めなかったとき（壊れている・読み込みに失敗した）は、「本体は 〜 しか持っていない」とも「アプリが古い」とも言わない。
    /// 版は足りているので、ただ「見つからない」。
    /// </summary>
    [Fact]
    public void WhenTheAppsCopyIsNotOlderTheReasonDoesNotBlameItsVersion()
    {
        var result = ModuleDependencies.Resolve(
            [Module("b", [Ref("Some.Library", V2)]), Module("c", [Ref(Api, V2)])],
            _ => false,
            (_, _) => false,
            name => name == "Some.Library" || name == Api ? V2 : null,
            NoMinimum);

        Assert.Equal(new DependencyProblem(DependencyProblemKind.Missing, Ref("Some.Library", V2), From: null), result.Failures["b"]);
        Assert.Equal(new DependencyProblem(DependencyProblemKind.Missing, Ref(Api, V2), From: null), result.Failures["c"]);
    }

    /// <summary>本体が持っていない名前は「見つからない」。</summary>
    [Fact]
    public void AReferenceTheAppDoesNotHaveAtAllIsStillMissing()
    {
        var result = ResolveInAnAppWithApi(V1, Module("b", [Ref("Newtonsoft.Json", V3)]));

        Assert.Equal(DependencyProblemKind.Missing, result.Failures["b"].Kind);
        Assert.Null(result.Failures["b"].Available);
    }

    [Fact]
    public void TheNewestCopyOfASharedDllIsTheOneShared()
    {
        var result = Resolve(
            Module("a", shared: Shared("a", "X.Shared", V1)),
            Module("b", shared: Shared("b", "X.Shared", V3)),
            Module("c", shared: Shared("c", "X.Shared", V2)));

        Assert.Equal("b", result.Shared["X.Shared"].Owner);
        Assert.Empty(result.Failures);
    }

    /// <summary>B が 2026.11.3 で作られ、載っているのが 2026.11.1 なら B は読まない（「A を更新して」）。</summary>
    [Fact]
    public void AModuleBuiltAgainstANewerSharedThanTheOneLoadedIsNotLoaded()
    {
        var result = Resolve(
            Module("a", [Ref("A.Shared", V1)], Shared("a", "A.Shared", V1)),
            Module("b", [Ref("A.Shared", V3)]));

        var problem = result.Failures["b"];
        Assert.Equal(DependencyProblemKind.TooOld, problem.Kind);
        Assert.Equal(Ref("A.Shared", V3), problem.Needed);
        Assert.Equal(V1, problem.Available);
        Assert.Equal("a", problem.Provider);
        Assert.Null(problem.From);
        Assert.False(result.Failures.ContainsKey("a"));
    }

    /// <summary>古い版で作ったものは、新しい方でそのまま動かす（契約は足していくだけ）。</summary>
    [Fact]
    public void AModuleBuiltAgainstAnOlderSharedRunsWithTheNewerOne()
    {
        var result = Resolve(
            Module("a", [Ref("A.Shared", V3)], Shared("a", "A.Shared", V3)),
            Module("b", [Ref("A.Shared", V1)]));

        Assert.Empty(result.Failures);
    }

    [Fact]
    public void TheSameVersionWithDifferentContentWarnsAndKeepsTheOneThatCameFirst()
    {
        var result = Resolve(
            Module("a", shared: Shared("a", "X.Shared", V2, sha: "AAAA")),
            Module("b", shared: Shared("b", "X.Shared", V2, sha: "BBBB")));

        Assert.Equal("a", result.Shared["X.Shared"].Owner);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("X.Shared", warning);
        Assert.Contains("a", warning);
        Assert.Contains("b", warning);
    }

    [Fact]
    public void TheSameVersionWithTheSameContentDoesNotWarn()
    {
        var result = Resolve(
            Module("a", shared: Shared("a", "X.Shared", V2, sha: "AAAA")),
            Module("b", shared: Shared("b", "X.Shared", V2, sha: "aaaa")));

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void AReferenceNobodyHasStopsTheModule()
    {
        var result = Resolve(Module("b", [Ref("System.Runtime", V1), Ref("A.Shared", V1)]));

        var problem = result.Failures["b"];
        Assert.Equal(DependencyProblemKind.Missing, problem.Kind);
        Assert.Equal(Ref("A.Shared", V1), problem.Needed);
    }

    [Fact]
    public void AReferenceInItsOwnFolderOrInTheHostIsFine()
    {
        var result = Resolve(
            [Module("b", [Ref("System.Runtime", V1), Ref("Microsoft.Data.Sqlite", V1)])],
            own: (id, reference) => id == "b" && reference.Name == "Microsoft.Data.Sqlite");

        Assert.Empty(result.Failures);
    }

    /// <summary>
    /// ローダーと同じく<b>共有を先に見る</b>。自分のフォルダに新しい写しがあっても、共有にある方が使われるので、そちらの版で決まる。
    /// </summary>
    [Fact]
    public void TheSharedCopyIsCheckedBeforeTheOwnFolder()
    {
        var result = Resolve(
            [
                Module("a", shared: Shared("a", "X.Shared", V1)),
                Module("b", [Ref("X.Shared", V3)]),
            ],
            own: (id, reference) => id == "b" && reference.Name == "X.Shared");

        Assert.Equal(DependencyProblemKind.TooOld, result.Failures["b"].Kind);
    }

    /// <summary>
    /// 読めないと決まったものの共有の dll は外し、決め直す。<b>別のモジュールが持っていた写しが代わりに載る</b>。
    /// </summary>
    [Fact]
    public void WhenTheOwnerCannotLoadAnotherCopyOfItsSharedTakesOver()
    {
        var result = Resolve(
            Module("a", [Ref("Nowhere", V1)], Shared("a", "X.Shared", V3)),
            Module("b", shared: Shared("b", "X.Shared", V1)),
            Module("c", [Ref("X.Shared", V1)]));

        Assert.Equal(DependencyProblemKind.Missing, result.Failures["a"].Kind);
        Assert.Equal("b", result.Shared["X.Shared"].Owner);
        Assert.False(result.Failures.ContainsKey("c"));
    }

    /// <summary>代わりの写しが古ければ、決め直したあとで使う側も止まる。</summary>
    [Fact]
    public void ACopyThatTakesOverButIsTooOldStopsItsUsersToo()
    {
        var result = Resolve(
            Module("a", [Ref("Nowhere", V1)], Shared("a", "X.Shared", V3)),
            Module("b", shared: Shared("b", "X.Shared", V1)),
            Module("c", [Ref("X.Shared", V3)]));

        Assert.Equal(DependencyProblemKind.TooOld, result.Failures["c"].Kind);
        Assert.Equal("b", result.Failures["c"].Provider);
    }

    /// <summary>持ち主が止まって代わりも無ければ、使う側は「見つからない」で止まる。</summary>
    [Fact]
    public void WhenTheOnlyOwnerCannotLoadItsUsersStopToo()
    {
        var result = Resolve(
            Module("a", [Ref("Nowhere", V1)], Shared("a", "X.Shared", V1)),
            Module("c", [Ref("X.Shared", V1)]));

        Assert.Equal(DependencyProblemKind.Missing, result.Failures["c"].Kind);
        Assert.Empty(result.Shared);
    }

    /// <summary>
    /// 共有の dll が参照してよいのは、本体が持つものとほかの共有の dll だけ。<b>持ち主のフォルダの dll には頼れない</b>
    /// （共有は既定のコンテキストに載るので、持ち主のフォルダは見えない）。
    /// </summary>
    [Fact]
    public void ASharedDllMayOnlyReferenceTheHostOrOtherSharedDlls()
    {
        var result = Resolve(
            [Module("a", shared: Shared("a", "X.Shared", V1, references: Ref("Newtonsoft.Json", V1)))],
            own: (id, reference) => id == "a" && reference.Name == "Newtonsoft.Json");

        var problem = result.Failures["a"];
        Assert.Equal(DependencyProblemKind.Missing, problem.Kind);
        Assert.Equal("X.Shared", problem.From);
        Assert.Equal(Ref("Newtonsoft.Json", V1), problem.Needed);
    }

    [Fact]
    public void ASharedDllMayReferenceAnotherSharedDll()
    {
        var result = Resolve(
            Module("a", shared: Shared("a", "X.Shared", V1, references: Ref("Y.Shared", V1))),
            Module("b", shared: Shared("b", "Y.Shared", V2)));

        Assert.Empty(result.Failures);
    }

    [Fact]
    public void ASharedDllReferencingANewerSharedDllStopsItsOwner()
    {
        var result = Resolve(
            Module("a", shared: Shared("a", "X.Shared", V1, references: Ref("Y.Shared", V3))),
            Module("b", shared: Shared("b", "Y.Shared", V1)));

        var problem = result.Failures["a"];
        Assert.Equal(DependencyProblemKind.TooOld, problem.Kind);
        Assert.Equal("X.Shared", problem.From);
        Assert.Equal("b", problem.Provider);
    }

    /// <summary>載らない写し（もっと新しい版が選ばれた）の参照は問わない。載るのは選ばれた方だけ。</summary>
    [Fact]
    public void ACopyThatIsNotChosenIsNotChecked()
    {
        var result = Resolve(
            Module("a", shared: Shared("a", "X.Shared", V1, references: Ref("Nowhere", V1))),
            Module("b", shared: Shared("b", "X.Shared", V2)));

        Assert.Empty(result.Failures);
        Assert.Equal("b", result.Shared["X.Shared"].Owner);
    }

    /// <summary>ライブラリ（入口の dll が無い）は共有の dll だけを渡す。使う側はライブラリの写しで動く。</summary>
    [Fact]
    public void ALibraryOnlySharesItsDlls()
    {
        var result = Resolve(
            Module("contracts", shared: Shared("contracts", "X.Shared", V2)),
            Module("a", [Ref("X.Shared", V2)]),
            Module("b", [Ref("X.Shared", V1)]));

        Assert.Empty(result.Failures);
        Assert.Equal("contracts", result.Shared["X.Shared"].Owner);
    }

    /// <summary>共有の dll が解けない持ち主を先に外す。外れた写しが「古すぎる」の元だった使う側は、決め直したあと自分のフォルダの写しで動く（同じ回でまとめて止めると、使う側は理由が消えたのに止まったままになる）。</summary>
    [Fact]
    public void AUserBlamedOnACopyWhoseOwnerFailsIsCheckedAgainAfterTheOwnerGoes()
    {
        var result = Resolve(
            [
                Module("c", shared: Shared("c", "Foo", V1, references: Ref("Nowhere", V1))),
                Module("d", [Ref("Foo", V2)]),
            ],
            own: (id, reference) => id == "d" && reference.Name == "Foo");

        Assert.Equal("Foo", result.Failures["c"].From);
        Assert.False(result.Failures.ContainsKey("d"));
        Assert.Empty(result.Shared);
    }

    /// <summary>入口の参照で落ちる持ち主が「古すぎる」の元なら、使う側はその回では外さず、持ち主が外れたあとで見直す。</summary>
    [Fact]
    public void AUserBlamedOnAProviderThatFailsTooWaitsForTheProviderToGo()
    {
        var result = Resolve(
            [
                Module("x", [Ref("Nowhere", V1)], Shared("x", "Foo", V1)),
                Module("d", [Ref("Foo", V2)]),
            ],
            own: (id, reference) => id == "d" && reference.Name == "Foo");

        Assert.Equal(DependencyProblemKind.Missing, result.Failures["x"].Kind);
        Assert.False(result.Failures.ContainsKey("d"));
    }

    /// <summary>
    /// <b>共有の dll が解けないものだけを先に外す</b>。q を外すと p の共有が解けなくなり、p が外れると d は自分のフォルダの写しで動ける。
    /// 同じ回で d まで外すと（その回の p はまだ落ちていないので待たない）、d は理由が消えたのに止まったままになる。
    /// </summary>
    [Fact]
    public void SharedFailuresAreRemovedBeforeEntryFailuresSoACascadeCanFreeAUser()
    {
        var result = Resolve(
            [
                Module("q", shared: Shared("q", "Bar", V2, references: Ref("Nowhere", V1))),
                Module("p", shared: Shared("p", "Foo", V1, references: Ref("Bar", V2))),
                Module("d", [Ref("Foo", V2)]),
            ],
            own: (id, reference) => id == "d" && reference.Name == "Foo");

        Assert.Equal("Bar", result.Failures["q"].From);
        Assert.Equal("Foo", result.Failures["p"].From);
        Assert.False(result.Failures.ContainsKey("d"));
    }

    /// <summary>お互いが相手の写しを「古すぎる」と言う形でも止まる（どちらも外して終わる）。</summary>
    [Fact]
    public void TwoModulesBlamingEachOtherBothStopAndTheLoopEnds()
    {
        var result = Resolve(
            Module("x", [Ref("B", V2)], Shared("x", "A", V1)),
            Module("y", [Ref("A", V2)], Shared("y", "B", V1)));

        Assert.Equal(DependencyProblemKind.TooOld, result.Failures["x"].Kind);
        Assert.Equal(DependencyProblemKind.TooOld, result.Failures["y"].Kind);
        Assert.Empty(result.Shared);
    }

    /// <summary>警告は最後の回の分だけ。途中で外れたものの写しについての警告は残さない。</summary>
    [Fact]
    public void AWarningAboutACopyWhoseOwnerWasRefusedIsDropped()
    {
        var result = Resolve(
            Module("x", [Ref("Nowhere", V1)], Shared("x", "Foo", V2, sha: "AAAA")),
            Module("y", shared: Shared("y", "Foo", V2, sha: "BBBB")));

        Assert.True(result.Failures.ContainsKey("x"));
        Assert.Equal("y", result.Shared["Foo"].Owner);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void TheProblemSaysWhatToDo()
    {
        var tooOld = new DependencyProblem(DependencyProblemKind.TooOld, Ref("A.Shared", V3), From: null, V1, "a").ToString();
        Assert.Contains("A.Shared 2026.11.3", tooOld);
        Assert.Contains("2026.11.1", tooOld);
        Assert.Contains("update a", tooOld);

        var missing = new DependencyProblem(DependencyProblemKind.Missing, Ref("Newtonsoft.Json", V1), From: "X.Shared").ToString();
        Assert.Contains("Newtonsoft.Json", missing);
        Assert.Contains("X.Shared", missing);

        Assert.Equal(
            "SpoitableToolKit.Abstractions 2026.11.3 is needed, but this app has 2026.11.1; " +
            "update the app (the module was built for a newer version of it)",
            new DependencyProblem(DependencyProblemKind.HostTooOld, Ref(Api, V3), From: null, V1).ToString());
        Assert.Equal(
            "SpoitableToolKit.Abstractions 2026.11.3 is needed (needed by the shared A.Shared), but this app has 2026.11.1; " +
            "update the app (the module was built for a newer version of it)",
            new DependencyProblem(DependencyProblemKind.HostTooOld, Ref(Api, V3), From: "A.Shared", V1).ToString());
    }
}
