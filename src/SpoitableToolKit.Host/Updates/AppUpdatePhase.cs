namespace SpoitableToolKit.Host.Updates;

/// <summary>アプリの更新がいまどこにいるか。画面へは camelCase の名前で渡る（<see cref="Models.AppUpdateView.Phase"/>）。</summary>
internal enum AppUpdatePhase
{
    /// <summary>まだ確かめていない（チャンネルを変えた直後も）。</summary>
    Idle,
    Checking,
    UpToDate,
    Available,
    /// <summary>確かめられなかった（繋がらない・まだ Release が無い）。前に見つけた版があれば、そちらを出したまま。</summary>
    CheckFailed,
    Downloading,
    /// <summary>落とし終えて Update.exe に頼んだ。アプリが閉じると入って起動し直す。</summary>
    Restarting,
    InstallFailed,
}
