namespace SpoitableToolKit.Abstractions.Config;

/// <summary>動きの行（<see cref="SpoitableSettingsAction"/>）の札の色。色はシェルがテーマから決める。</summary>
public enum SpoitableSettingsTone
{
    /// <summary>色を付けない（確認中・確かめられなかった・使っていない）。</summary>
    Neutral,

    /// <summary>問題ない（許可されています）。</summary>
    Good,

    /// <summary>気を付けてほしい（見当たりません）。直す手があるならボタンを出す。</summary>
    Warn,

    /// <summary>このままでは動かない（ブロックされています）。</summary>
    Bad,
}
