using SpoitableToolKit.Abstractions.Config.Attributes;
using SpoitableToolKit.Abstractions.Events.Config;

namespace SpoitableToolKit.Abstractions.Config;

/// <summary>
/// モジュールの設定。<b>ふつうのクラスに public なフィールドを並べるだけ</b>で、
/// ホストが reflection で読んで設定画面を組み立てる。
/// <code>
/// sealed class Config : IModuleConfig
/// {
///     [SpoitableSettingsLabel("再生失敗のたびに聞く")]
///     public bool AskOnFailure = true;
///
///     [SpoitableSettingsLabel("最大リトライ数")]
///     [SpoitableSettingsRange(1, 10)]
///     public int MaxRetryCount = 3;
/// }
/// </code>
/// <para>
/// <b>インスタンスはホストと共有する。</b> 画面で変えた値はこのインスタンスへ直接書かれるので、
/// モジュールは使うときに読めばよい。「値が変わった」を待ち受ける必要はない
/// （変更を契機に何かしたい・値を確かめたいときだけ <see cref="IConfigEventListener{TConfig}"/> を <see cref="IModuleHost.Config"/> に足す）。
/// </para>
/// <para>
/// 保存できるのは <b>JSON にできるもの全部</b>。配列も入れ子のクラスも通る。
/// 画面に専用のコントロールが出るのは、ホストが形を知っている型
/// （<c>bool</c> / 整数 / 小数 / <c>string</c> / <c>enum</c> / それらの配列）だけで、
/// それ以外は JSON のテキスト欄になる。<b>保存されるのに画面に無い、という状態は作らない</b>
/// （隠したいときは <see cref="SpoitableSettingsHiddenAttribute"/> を付ける）。
/// </para>
/// <para>
/// 値は <see cref="ISpoitableModule.OnInit"/> より<b>前に</b>読み込まれる。OnInit の時点で
/// 保存済みの値が入っている前提で書いてよい。
/// </para>
/// </summary>
public interface IModuleConfig
{
    /// <summary>
    /// その項目に入れられる値の候補。設定画面が入力の下に出す。
    /// <b>候補であって選択肢ではない</b> — ここに無い値も打てる
    /// （選択肢を絞りたいなら <c>enum</c> にする）。
    /// <para>
    /// 決め打ちで書けない候補のためにある。「いま読み込まれているモジュールの Id」や
    /// 「見つかっている BLE デバイスの名前」のように、<b>実行時にしか分からないもの</b>が対象。
    /// 設定画面を開くたびに呼ばれるので、その時点の答えを返せばよい。
    /// </para>
    /// <para>
    /// UI のスレッドから呼ばれるので<b>ここで待たないこと</b>。手元にあるものを返すだけにする。
    /// 例外はホストが握り潰して「候補なし」として扱う
    /// （候補の失敗で設定画面ごと開かなくなる方が困る）。
    /// </para>
    /// <para>
    /// <b>呼ばれるのは本物の設定</b>（実行時に入れた一覧などはここにしか無い）。打っている途中の値は写しにだけ入っているので、
    /// 候補をほかの設定の値で変えても、変わるのは「適用」のあと。
    /// </para>
    /// </summary>
    /// <param name="memberName">
    /// <b>C# 側の名前</b>（保存名ではない）。<c>nameof</c> と突き合わせられるように。
    /// </param>
    /// <returns>候補。出さないなら <c>null</c>。</returns>
    IReadOnlyList<string>? SuggestValuesFor(string memberName) => null;
}
