namespace SpoitableToolKit.Abstractions.Config;

/// <summary>
/// <b>設定のひとまとまり。</b> 設定のクラスの中にこういう型のメンバーを置くと、
/// 設定画面ではセクションとして並ぶ。<b>これ自体は出し入れをしない</b> —
/// 項目分けだけで、中身は常に出る。
/// <code>
/// sealed class Config : IModuleConfig
/// {
///     // 平らな項目はそのまま持てる
///     public bool AskOnFailure = true;
///
///     // これがセクション。中身はふつうに並べるだけ
///     public ServerSettings Server { get; } = new();
/// }
/// </code>
/// <para>
/// 出し入れしたいなら、実装する契約を選ぶ:
/// </para>
/// <list type="bullet">
///   <item><see cref="ITogglableSettings"/> — 入/切のスイッチ。<b>画面にチェックが出る</b></item>
///   <item><see cref="IConditionalSettings"/> — 条件を式で書く。<b>画面には何も足さない</b></item>
/// </list>
/// <para>
/// <b>何段でも入れ子にできる。</b> セクションの中にセクションを置いてよい。
/// 親が出ないなら、その下は全部出ない。
/// </para>
/// <para>
/// <b>隠れても中の値は消えない。</b> ふつうのメンバーなので保存され、また出れば元の値が見える。
/// </para>
/// </summary>
public interface ISpoitableSettings;
