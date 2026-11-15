namespace SpoitableToolKit.Modules.Hrs.Config;

/// <summary>
/// アバターへ送るパラメータの組。一度に使うのは 1 つ。
/// 設定画面の選択欄にメンバー名がそのまま出て、設定ファイルにも文字列で残るので、変えると利用者の選択が読めなくなる。
/// </summary>
public enum HrsOscPreset
{
    VRCHR,
    VRCOSC,
}
