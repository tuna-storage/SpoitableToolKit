namespace SpoitableToolKit.Abstractions.Ui;

/// <summary>
/// 自分のページから呼ばれる口。<b><see cref="IModuleUi.RegisterEndpoint"/> で登録したときだけ呼ばれる</b>
/// （モジュールのクラスが実装しているだけでは呼ばれない。別のクラスに実装して登録してもよい）。
/// <para>
/// 呼び出しは<b>必ずモジュール Id 宛にルーティングされる</b>。ページは
/// <c>spoitable://&lt;Id&gt;/</c> という専用の origin で動き、シェルは <c>postMessage</c> の
/// <b>発信元 origin</b> から宛先 Id を決める。モジュール側が Id を名乗る余地は無い。
/// </para>
/// <para>
/// やり取りは JSON 文字列。中身の取り決めはモジュールの自由だが、
/// <b>正となる状態を JS 側に置かないこと</b>。サーフェスが 2 つ（デスクトップ窓 / VR オーバーレイ）
/// ある以上、同じページが別々の JS コンテキストで動く。状態は C# 側に持つ。
/// </para>
/// </summary>
public interface IUiEndpoint
{
    /// <param name="method">モジュール内での操作名。</param>
    /// <param name="payload">JSON 文字列。引数が無ければ空文字。</param>
    /// <param name="cancellationToken">呼び出しを取りやめる合図。</param>
    /// <returns>JSON 文字列。例外を投げた場合はホストがエラーとして UI へ返す。</returns>
    Task<string> HandleUiCallAsync(string method, string payload, CancellationToken cancellationToken = default);
}
