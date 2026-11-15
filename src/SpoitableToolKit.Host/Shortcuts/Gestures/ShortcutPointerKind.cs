namespace SpoitableToolKit.Host.Shortcuts.Gestures;

internal enum ShortcutPointerKind
{
    /// <summary>押した（新しい押しの始まり。番号が増える）。</summary>
    Down,

    Move,

    Up,

    /// <summary>離さずに途切れた（ページが押しを見失った・タイルが消えた・Layout Mode に入った）。</summary>
    Cancel,
}
