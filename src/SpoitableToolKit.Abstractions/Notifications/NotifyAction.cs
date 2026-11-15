namespace SpoitableToolKit.Abstractions.Notifications;

/// <summary>
/// 通知に添える押せるもの。「フォルダを開く」のように、<b>その通知から直に片付く</b>操作に使う。
/// <para>
/// <b>押されたら通知は閉じる。</b> 決着を付けるためのものなので、残っていると押し直せてしまう。
/// </para>
/// <para>
/// <see cref="Invoke"/> は<b>裏のスレッドで走る</b>ので、待つ処理を書いてよい。
/// 例外はホストが握ってログに残す（画面から消えて何も分からない、を作らないため）。
/// </para>
/// </summary>
public sealed record NotifyAction(string Label, Action Invoke);
