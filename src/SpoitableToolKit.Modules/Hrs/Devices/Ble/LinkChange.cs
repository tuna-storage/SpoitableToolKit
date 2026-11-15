namespace SpoitableToolKit.Modules.Hrs.Devices.Ble;

/// <summary>線の上がり下がりで、繋がりがどう変わったか。</summary>
internal enum LinkChange
{
    /// <summary>変わっていない（同じ知らせの 2 回目・掴む前・手放した後）。</summary>
    None,

    /// <summary>線が落ちた。掴んだ物はそのままだが、もう「繋がっている」とは言わない。</summary>
    Lost,

    /// <summary>線が戻った。同じ相手にまた繋がっている。</summary>
    Back,
}
