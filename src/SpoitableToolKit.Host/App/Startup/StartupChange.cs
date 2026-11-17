namespace SpoitableToolKit.Host.App.Startup;

/// <summary>登録を設定に合わせるためにすること（<see cref="StartupRegistration.Plan"/>）。</summary>
internal enum StartupChange
{
    /// <summary>もう合っている。</summary>
    None,

    /// <summary>無い・違う場所を指している。書き直す。</summary>
    Write,

    /// <summary>切なのに残っている。消す。</summary>
    Remove,
}
