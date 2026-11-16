namespace SpoitableToolKit.Modules.Hrs.Broadcast;

/// <summary>最後に流した内容と、それが何通目か（1 から）。パネルのプレビューは、番号で新しい 1 通かを見分ける（同じ数字が続いても 1 通は 1 通）。</summary>
internal sealed record HrsBroadcastSent(HrsBroadcastState State, long Number);
