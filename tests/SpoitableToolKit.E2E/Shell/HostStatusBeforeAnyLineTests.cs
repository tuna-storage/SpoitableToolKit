using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Shell;

/// <summary>このクラスのアプリには誰も行を流さない。行を流す試験をこのクラスに足さない。</summary>
public sealed class HostStatusBeforeAnyLineTests(AppFixture app) : IClassFixture<AppFixture>
{
    [Fact]
    public async Task TheTitleBarSaysTheLogIsBeingReadBeforeAnyLine()
    {
        await Assertions.Expect(app.Page.Locator("#hostStatus"))
            .ToHaveTextAsync("VRChat のログを監視中", new() { Timeout = 15_000 });
    }
}
