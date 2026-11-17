namespace SpoitableToolKit.E2E.Desktop;

/// <summary>設定は入・登録はこのアプリを指したまま、Windows の「スタートアップ アプリ」で切られた状態で始めるアプリ。</summary>
public sealed class TurnedOffInWindowsAppFixture : RunKeyAppFixture
{
    protected override string StartingRegistration => Expected;

    protected override string? StartingApproval => DisabledMark;
}
