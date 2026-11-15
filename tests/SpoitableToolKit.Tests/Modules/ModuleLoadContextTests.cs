using SpoitableToolKit.Abstractions.Modules;
using Xunit;

namespace SpoitableToolKit.Tests.Modules;

public sealed class ModuleLoadContextTests
{
    [Fact]
    public void AnAssemblyTheAppDidNotLoadAsAModuleHasNoModuleFolder()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => IModuleLoadContext.DirectoryOf(typeof(ModuleLoadContextTests).Assembly));

        Assert.Equal(
            "SpoitableToolKit.Tests was not loaded by SpoitableToolKit as a module, so it has no module folder. " +
            "ModuleInfo.ModuleDirectory is known only while the app runs the module (the app loads it from modules/<Id>/). " +
            "Code that runs outside the app, such as a unit test of the module, has to be given the folder in another way.",
            thrown.Message);
    }
}
