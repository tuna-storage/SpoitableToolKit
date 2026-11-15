using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Ui.Licenses;
using SpoitableToolKit.Host.Ui.Push;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Licenses;

public sealed class LicenseTextsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "stk-licenses-" + Guid.NewGuid().ToString("N"));

    public LicenseTextsTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void BothFilesAreReadEachTimeAndReachTheScreenByName()
    {
        File.WriteAllText(Path.Combine(_directory, "LICENSE"), "MIT License (c) 2026");
        File.WriteAllText(Path.Combine(_directory, "THIRD-PARTY-NOTICES.md"), "# Third-party notices");
        var texts = new LicenseTexts(_directory, NullLogger.Instance);

        using (var json = JsonDocument.Parse(UiBroadcast.Json(texts.Read())))
        {
            Assert.Equal("MIT License (c) 2026", json.RootElement.GetProperty("app").GetString());
            Assert.Equal("# Third-party notices", json.RootElement.GetProperty("thirdParty").GetString());
        }

        File.WriteAllText(Path.Combine(_directory, "THIRD-PARTY-NOTICES.md"), "# changed");
        Assert.Equal("# changed", texts.Read().ThirdParty);
    }

    [Fact]
    public void AMissingFileIsNullAndTheOtherIsStillRead()
    {
        File.WriteAllText(Path.Combine(_directory, "LICENSE"), "MIT");

        var view = new LicenseTexts(_directory, NullLogger.Instance).Read();

        Assert.Equal(("MIT", (string?)null), (view.App, view.ThirdParty));
    }
}
