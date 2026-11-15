using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Modules;
using SpoitableToolKit.Host.ExternalModules;
using Xunit;

namespace SpoitableToolKit.Tests.ExternalModules;

/// <summary>失敗の文も試験で固定する。モジュールを書くのは AI で、頼りはこの文だけ（正しい名前の形・登録されている名前・どの dll から来た型か）。</summary>
public sealed class ExternalModuleRegistryTests
{
    private interface IGreeter
    {
        string Greet();
    }

    private interface ILoudGreeter : IGreeter
    {
        string Shout();
    }

    private interface IClock
    {
        int Hour { get; }
    }

    private sealed class Greeter(string text = "hi") : ILoudGreeter
    {
        public string Greet() => text;

        public string Shout() => text.ToUpperInvariant();
    }

    private sealed class Clock : IClock
    {
        public int Hour => 9;
    }

    private sealed class StubModule(string id) : ISpoitableModule
    {
        public string Id => id;

        public string DisplayName => id;

        public bool OnInit(IModuleHost host) => true;
    }

    private static ExternalModuleRegistry NewRegistry() => new(NullLoggerFactory.Instance);

    [Fact]
    public void ARegisteredModuleIsTakenByItsName()
    {
        var registry = NewRegistry();
        registry.Register<IGreeter>(new StubModule("provider"), "provider.greeter", new Greeter());

        var reference = registry.GetRequired<IGreeter>("provider.greeter");

        Assert.True(reference.IsAvailable);
        Assert.Equal("hi", reference.Instance!.Greet());
        Assert.Equal("hi", registry.GetOptional<IGreeter>("provider.greeter")!.Instance!.Greet());
    }

    [Theory]
    [InlineData("greeter")]
    [InlineData("other.greeter")]
    [InlineData("providerx.greeter")]
    [InlineData("provider.")]
    [InlineData("provider")]
    [InlineData("provider.Greeter")]
    [InlineData("provider.gre eter")]
    [InlineData("provider.greeter.")]
    [InlineData("provider.greeter\n")]
    [InlineData("provider.api.v2")]     // Id のあとに . は入れない（持ち主が 1 つに決まらなくなる）
    [InlineData("provider..x")]
    [InlineData("provider.-x")]
    [InlineData("provider._x")]
    [InlineData("provider.x-")]
    [InlineData("Provider.greeter")]
    [InlineData("")]
    public void AModuleNameMustStartWithTheRegisteringModulesId(string name)
    {
        var registry = NewRegistry();

        var error = Assert.Throws<ArgumentException>(() => registry.Register<IGreeter>(new StubModule("provider"), name, new Greeter()));

        Assert.Contains("the module 'provider'", error.Message);
        Assert.Contains("for example 'provider.api'", error.Message);
        Assert.Contains("const string ModuleIdentity = \"provider.api\";", error.Message);
        Assert.Null(registry.GetOptional<IGreeter>(name));
    }

    [Theory]
    [InlineData("provider.api")]
    [InlineData("provider.api-v2")]
    [InlineData("provider.heart-rate_2")]
    [InlineData("provider.a")]
    public void AModuleNameAfterTheIdIsLowercaseLettersDigitsAndSeparators(string name)
    {
        var registry = NewRegistry();

        registry.Register<IGreeter>(new StubModule("provider"), name, new Greeter());

        Assert.True(registry.GetRequired<IGreeter>(name).IsAvailable);
    }

    /// <summary>Id には <c>.</c> が入りうるが、Id のあとに <c>.</c> を入れさせないので、名前の持ち主は「最後の <c>.</c> の手前」で 1 つに決まる。</summary>
    [Fact]
    public void AModuleWhoseIdHasDotsOwnsItsNamesAlone()
    {
        var registry = NewRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register<IGreeter>(new StubModule("hrs"), "hrs.extra.api", new Greeter()));
        registry.Register<IGreeter>(new StubModule("hrs.extra"), "hrs.extra.api", new Greeter("extra"));

        Assert.Equal("extra", registry.GetRequired<IGreeter>("hrs.extra.api").Instance!.Greet());
        var missing = Assert.Throws<InvalidOperationException>(() => registry.GetRequired<IGreeter>("hrs.extra.other"));
        Assert.Contains("the module 'hrs.extra' is not installed", missing.Message);
    }

    /// <summary>型引数を書き忘れると実体の型で登録され、使う側はその型を参照できないので、登録のときに断る。</summary>
    [Fact]
    public void AModuleIsRegisteredAsAnInterfaceNotAsItsImplementation()
    {
        var registry = NewRegistry();
        var greeter = new Greeter();

        var error = Assert.Throws<ArgumentException>(() => registry.Register(new StubModule("provider"), "provider.greeter", greeter));

        Assert.Contains("is not an interface", error.Message);
        Assert.Contains("Register<IYourContract>(this, name, implementation)", error.Message);
        Assert.Null(registry.GetOptional<IGreeter>("provider.greeter"));
    }

    [Theory]
    [InlineData("greeter")]
    [InlineData(".greeter")]
    [InlineData("provider.")]
    [InlineData("provider.Greeter")]
    public void AskingForSomethingThatIsNotAModuleNameSaysSo(string name)
    {
        var error = Assert.Throws<InvalidOperationException>(() => NewRegistry().GetRequired<IGreeter>(name));

        Assert.Contains($"'{name}' is not a module name", error.Message);
        Assert.Contains("for example 'hrs.api'", error.Message);
        Assert.DoesNotContain("is not installed", error.Message);
    }

    [Fact]
    public void TheSameNameCannotBeRegisteredTwice()
    {
        var registry = NewRegistry();
        var owner = new StubModule("provider");
        registry.Register<IGreeter>(owner, "provider.greeter", new Greeter("first"));

        var error = Assert.Throws<InvalidOperationException>(() => registry.Register<IClock>(owner, "provider.greeter", new Clock()));

        Assert.Contains("'provider.greeter' is already registered (by 'provider' as " + typeof(IGreeter).FullName + ")", error.Message);
        Assert.Contains("'provider.greeter2'", error.Message);
        Assert.Equal("first", registry.GetRequired<IGreeter>("provider.greeter").Instance!.Greet());
    }

    [Fact]
    public void TheSameContractCanBeRegisteredUnderSeveralNamesAndListed()
    {
        var registry = NewRegistry();
        registry.Register<IGreeter>(new StubModule("one"), "one.greeter", new Greeter("from one"));
        registry.Register<IGreeter>(new StubModule("two"), "two.greeter", new Greeter("from two"));
        registry.Register<IClock>(new StubModule("two"), "two.clock", new Clock());

        var greeters = registry.GetAll<IGreeter>();

        var names = greeters.Keys.Order().ToArray();
        Assert.Equal(["one.greeter", "two.greeter"], names);
        Assert.Equal("from two", greeters["two.greeter"].Instance!.Greet());
        Assert.Empty(NewRegistry().GetAll<IGreeter>());
    }

    [Fact]
    public void AModuleCanBeTakenAsAContractItsImplementationAlsoHas()
    {
        var registry = NewRegistry();
        registry.Register<ILoudGreeter>(new StubModule("provider"), "provider.greeter", new Greeter());

        Assert.Equal("hi", registry.GetRequired<IGreeter>("provider.greeter").Instance!.Greet());
        Assert.Single(registry.GetAll<IGreeter>());
    }

    [Fact]
    public void AMissingRequiredModuleListsWhatIsRegistered()
    {
        var registry = NewRegistry();

        var nothing = Assert.Throws<InvalidOperationException>(() => registry.GetRequired<IGreeter>("provider.greeter"));
        Assert.StartsWith("No module is registered as 'provider.greeter'. No module has registered any module.", nothing.Message);
        Assert.Contains("the module 'provider' is not installed, is turned off, or failed to start", nothing.Message);
        Assert.Contains("ModuleIdentity", nothing.Message);
        Assert.Contains("OnAllModulesLoaded", nothing.Message);
        Assert.Contains("GetOptional", nothing.Message);

        registry.Register<IClock>(new StubModule("two"), "two.clock", new Clock());
        var otherType = Assert.Throws<InvalidOperationException>(() => registry.GetRequired<IGreeter>("provider.greeter"));
        Assert.Contains($"Nothing is registered as {typeof(IGreeter).FullName}. Registered names: two.clock.", otherType.Message);

        registry.Register<IGreeter>(new StubModule("provider"), "provider.greeting", new Greeter());
        registry.Register<IGreeter>(new StubModule("another"), "another.greeter", new Greeter());
        var sameType = Assert.Throws<InvalidOperationException>(() => registry.GetRequired<IGreeter>("provider.greeter"));
        Assert.Contains($"Registered as {typeof(IGreeter).FullName}: another.greeter, provider.greeting.", sameType.Message);
    }

    [Fact]
    public void AMissingOptionalModuleComesBackNull()
    {
        var registry = NewRegistry();

        Assert.Null(registry.GetOptional<IGreeter>("provider.greeter"));
    }

    /// <summary>任意の取り方でも null にしない（黙って「居ない」にすると、間違いに気付けない）。</summary>
    [Fact]
    public void AskingWithADifferentContractSaysWhichOneItWasRegisteredWith()
    {
        var registry = NewRegistry();
        registry.Register<IGreeter>(new StubModule("provider"), "provider.greeter", new Greeter());

        var required = Assert.Throws<InvalidOperationException>(() => registry.GetRequired<IClock>("provider.greeter"));
        var optional = Assert.Throws<InvalidOperationException>(() => registry.GetOptional<IClock>("provider.greeter"));

        Assert.Equal(required.Message, optional.Message);
        Assert.Contains($"'provider.greeter' is registered as {typeof(IGreeter).FullName} (by 'provider'), which is not a {typeof(IClock).FullName}", required.Message);
        Assert.Contains("GetRequired<IGreeter>(\"provider.greeter\")", required.Message);
        Assert.DoesNotContain("loaded twice", required.Message);
    }

    /// <summary>同じ名前の型が 2 つある（契約の dll の写しを両方が持つ・別の版）のは、症状（「登録したのに取れない」）から原因が分からないので、どちらの dll から来た型かと直し方を出す。</summary>
    [Fact]
    public void AContractThatWasLoadedTwiceIsToldApartFromADifferentContract()
    {
        var (registered, implementation) = EmitContract("ContractCopyOne");
        var (asked, _) = EmitContract("ContractCopyTwo");
        var registry = NewRegistry();
        Call(registry, nameof(IExternalModuleRegistry.Register), registered, new StubModule("provider"), "provider.thing", implementation);

        var error = Assert.Throws<InvalidOperationException>(() => Call(registry, nameof(IExternalModuleRegistry.GetRequired), asked, "provider.thing"));

        Assert.Contains("registered as Contracts.IThing from ContractCopyOne 1.0.0.0", error.Message);
        Assert.Contains("asked for as Contracts.IThing from ContractCopyTwo 2.0.0.0", error.Message);
        Assert.Contains("the contract assembly was loaded twice (a copy, or another version)", error.Message);
        Assert.Contains("modules/provider/shared/", error.Message);

        // 一覧でも黙って抜かさない（抜かすと「誰も登録していない」に見える）。
        var listing = Assert.Throws<InvalidOperationException>(() => Call(registry, nameof(IExternalModuleRegistry.GetAll), asked));
        Assert.Equal(error.Message, listing.Message);
    }

    [Fact]
    public void LosingTheProviderInvalidatesReferencesAlreadyTakenAndFreesTheName()
    {
        var registry = NewRegistry();
        var owner = new StubModule("provider");
        registry.Register<IGreeter>(owner, "provider.greeter", new Greeter());

        // 消費側が先に参照を持っている状態で提供元が落ちる、という順番が肝。
        var reference = registry.GetRequired<IGreeter>("provider.greeter");
        var listed = registry.GetAll<IGreeter>()["provider.greeter"];
        registry.RevokeAll(owner);

        Assert.False(reference.IsAvailable);
        Assert.Null(reference.Instance);
        Assert.Null(listed.Instance);
        Assert.Null(registry.GetOptional<IGreeter>("provider.greeter"));
        Assert.Empty(registry.GetAll<IGreeter>());

        // 名前は空く（落ちたものの登録が残って、次の登録を邪魔しない）。
        registry.Register<IGreeter>(new StubModule("provider"), "provider.greeter", new Greeter("again"));
        Assert.Equal("again", registry.GetRequired<IGreeter>("provider.greeter").Instance!.Greet());
    }

    [Fact]
    public void LosingAnotherProviderChangesNothing()
    {
        var registry = NewRegistry();
        registry.Register<IGreeter>(new StubModule("provider"), "provider.greeter", new Greeter());

        registry.RevokeAll(new StubModule("provider"));

        Assert.True(registry.GetRequired<IGreeter>("provider.greeter").IsAvailable);
    }

    /// <summary>同じ名前（<c>Contracts.IThing</c>）の契約と実体を別々のアセンブリに作る（契約の dll が二重に読み込まれた形）。</summary>
    private static (Type Contract, object Implementation) EmitContract(string assemblyName)
    {
        var version = assemblyName.EndsWith("One", StringComparison.Ordinal) ? new Version(1, 0, 0, 0) : new Version(2, 0, 0, 0);
        var module = AssemblyBuilder
            .DefineDynamicAssembly(new AssemblyName(assemblyName) { Version = version }, AssemblyBuilderAccess.Run)
            .DefineDynamicModule(assemblyName);

        var contract = module.DefineType("Contracts.IThing", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract).CreateType();
        var implementation = module.DefineType("Contracts.Thing", TypeAttributes.Public | TypeAttributes.Class);
        implementation.AddInterfaceImplementation(contract);
        implementation.DefineDefaultConstructor(MethodAttributes.Public);

        return (contract, Activator.CreateInstance(implementation.CreateType())!);
    }

    /// <summary>型引数を実行時に決めて呼ぶ（上で作った型はコンパイル時には無いので）。中で投げたものは、そのまま外へ出す。</summary>
    private static object? Call(ExternalModuleRegistry registry, string method, Type contract, params object[] arguments)
    {
        try
        {
            return typeof(ExternalModuleRegistry).GetMethod(method)!.MakeGenericMethod(contract).Invoke(registry, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
