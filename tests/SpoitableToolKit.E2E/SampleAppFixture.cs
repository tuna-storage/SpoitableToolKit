namespace SpoitableToolKit.E2E;

/// <summary>AppFixture と同じアプリに、本物のモジュール sample（tests/fixtures/SampleModule）を入れて起動する。</summary>
public sealed class SampleAppFixture : AppFixture
{
    protected override void Prepare() => PlaceSampleModule();
}
