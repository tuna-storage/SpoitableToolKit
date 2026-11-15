namespace SpoitableToolKit.Abstractions.Events.Config;

/// <summary>断った値と、その理由（設定画面のその欄の下に出る）。作るのはホストだけ（<see cref="ConfigChangingEventParams{TConfig}.Reject"/> から）。</summary>
public sealed record ConfigRejection
{
    /// <summary>断った欄（設定のメンバーの名前）。</summary>
    public required string Name { get; init; }

    /// <summary>断った理由。</summary>
    public required string Reason { get; init; }
}
