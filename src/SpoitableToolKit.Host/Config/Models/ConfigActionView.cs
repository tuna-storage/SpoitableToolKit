namespace SpoitableToolKit.Host.Config.Models;

/// <summary>
/// 動きの行（<see cref="SpoitableToolKit.Abstractions.Config.SpoitableSettingsAction"/>）の、画面へ渡す姿。
/// 設定の材料（<c>getConfigs</c> の <c>state</c>。JSON 文字列）と push（<c>configAction</c>）の両方がこれを <c>UiBroadcast.Json</c> で送るので、どちらの道でも同じ形で届く。
/// </summary>
/// <param name="Seq">作った順の番号（アプリの中で増えるだけ）。画面は大きい方を残す — 設定の材料を取りに行っている間に届いた push を、古い材料で描き戻さない。</param>
/// <param name="Tone"><c>neutral</c> / <c>good</c> / <c>warn</c> / <c>bad</c>。</param>
/// <param name="Running">ボタンの分が走っている。画面はボタンを押せなくする（面は別々なので、押した面だけでなく全部の面で）。</param>
internal sealed record ConfigActionView(
    long Seq,
    string Tone,
    string Status,
    string? Detail,
    string? Button,
    string? ButtonNote,
    string? Copy,
    bool Running);
