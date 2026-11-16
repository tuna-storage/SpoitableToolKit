using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Host.Config.Models;

namespace SpoitableToolKit.Host.Config.Actions;

/// <summary>
/// 本物の設定の、動きの行 1 つ（<see cref="SpoitableSettingsAction"/>）。走っている間の印と、結び忘れの警告（1 回だけ）を持つ。
/// <para>
/// 写し（下見・listener に見せるもの）はこれを持たない。写しの行は <c>new</c> で作り直されたもので結ばれていないので、
/// 画面はどの道（<c>getConfigs</c>・下見・push）でもここ（本物）から読む。
/// </para>
/// </summary>
internal sealed class ConfigAction(ConfigMember member, SpoitableSettingsAction row, ILogger logger)
{
    /// <summary>姿を作った順。行をまたいで増えるだけ（<see cref="ConfigActionView.Seq"/>）。</summary>
    private static long _seq;

    private int _running;

    private int _warnedUnbound;

    public ConfigMember Member => member;

    public SpoitableSettingsAction Row => row;

    /// <summary>画面の宛先（<c>data-key</c>）。</summary>
    public string Path => member.Path;

    /// <summary>ログに出すモジュールの Id。ストアが迎え入れたときに入れる。</summary>
    public string? ModuleId { get; set; }

    public bool Running => Volatile.Read(ref _running) == 1;

    private string Module => ModuleId ?? "(no module)";

    private string OwnerType => member.Owner.GetType().Name;

    /// <summary>
    /// いまの姿。<b>モジュールの describe はここで呼ぶ</b>（どのスレッドからでも来る）。
    /// 結んでいない・投げた・札が空のときはホストの文言で埋める — 1 行の不具合で設定画面ごと開かなくなるのを避ける（候補の <c>Suggest</c> と同じ扱い）。
    /// </summary>
    public ConfigActionView View(ILocalizer text)
    {
        var seq = Interlocked.Increment(ref _seq);
        var running = Running;

        if (!row.IsBound)
        {
            // 画面を開くたびに描くので、警告は 1 回だけ。
            if (Interlocked.Exchange(ref _warnedUnbound, 1) == 0)
                logger.LogWarning(
                    "{Module}: {Type}.{Member} (SpoitableSettingsAction) was never bound, so the settings screen shows it as unavailable. " +
                    "Call {Name}.Bind(describe, run) once in OnInit, on the settings the module returns from ISpoitableModule.Config",
                    Module, OwnerType, member.Name, member.Name);

            return HostView(seq, "neutral", text["host.settings.action.unavailable"]);
        }

        SpoitableSettingsActionState? state;
        try
        {
            state = row.Describe(text);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Module}: could not read the state of {Type}.{Member}", Module, OwnerType, member.Name);
            return HostView(seq, "bad", text["host.settings.action.failed"]);
        }

        if (state is null || string.IsNullOrWhiteSpace(state.Status))
        {
            logger.LogWarning(
                "{Module}: {Type}.{Member} returned no Status; return a SpoitableSettingsActionState with a short Status to show",
                Module, OwnerType, member.Name);
            return HostView(seq, "bad", text["host.settings.action.failed"]);
        }

        var button = Blank(state.Button);

        return new ConfigActionView(
            Seq: seq,
            Tone: ToneOf(state.Tone),
            Status: state.Status,
            Detail: Blank(state.Detail),
            Button: button,

            // 注記はボタンのためのもの。ボタンが無いのに出すと、何の注記か分からない。
            ButtonNote: button is null ? null : Blank(state.ButtonNote),
            Copy: Blank(state.Copy),
            Running: running);
    }

    /// <summary>
    /// ボタンの分を裏で走らせる。<b>走っている間・結んでいないときは何もしない</b>（false）。
    /// 走り始めと終わりに <paramref name="changed"/> を呼ぶ（全部の面のボタンを押せない / 押せるにする）。ここでは待たない。
    /// モジュールの例外はログに残すだけ（結果はモジュールが状態で見せる）。
    /// </summary>
    public bool TryStart(Action changed)
    {
        if (!row.IsBound) return false;
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;

        changed();

        _ = Task.Run(async () =>
        {
            try
            {
                if (row.Run() is { } task) await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Module}: the action of {Type}.{Member} failed", Module, OwnerType, member.Name);
            }
            finally
            {
                Volatile.Write(ref _running, 0);
                changed();
            }
        });

        return true;
    }

    private static ConfigActionView HostView(long seq, string tone, string status) =>
        new(Seq: seq, Tone: tone, Status: status, Detail: null, Button: null, ButtonNote: null, Copy: null, Running: false);

    private static string ToneOf(SpoitableSettingsTone tone) => tone switch
    {
        SpoitableSettingsTone.Good => "good",
        SpoitableSettingsTone.Warn => "warn",
        SpoitableSettingsTone.Bad => "bad",
        _ => "neutral",
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
