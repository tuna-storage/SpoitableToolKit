using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Config.Attributes;
using SpoitableToolKit.Modules.Hrs.Avatar;
using SpoitableToolKit.Modules.Hrs.Devices.Models;

namespace SpoitableToolKit.Modules.Hrs.Config;

/// <summary>
/// hrs の設定。アバターのパラメータ名は、どちらのプリセットも 9 個とも変えられる。
/// 保存するのは名前だけで、<c>/avatar/parameters/</c> は送るときに足す（<see cref="ParameterNames"/>）。
/// <para>
/// 入れ子は readonly フィールドで持つ。get だけのプロパティでも動くが、reflection はプロパティを先に返すので、
/// 入れ子が全部いちばん上に来てしまう。宣言の順が保たれるフィールドを使う。
/// </para>
/// </summary>
public sealed class HrsConfig : IModuleConfig
{
    // 並ぶ順がそのまま画面の順。束ね（SpoitableSettingsGroup）は見せ方だけで、config.json は名前で読むので並べ替えても前の値が読める。

    [SpoitableSettingsGroup("hrs.settings.group.device")]
    [SpoitableSettingsLabelLocalize("hrs.settings.device.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.device.description")]
    public string Device = "";

    [SpoitableSettingsGroup("hrs.settings.group.device")]
    [SpoitableSettingsLabelLocalize("hrs.settings.noSignalSeconds.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.noSignalSeconds.description")]
    [SpoitableSettingsRange(3, 120)]
    public int NoSignalSeconds = 10;

    [SpoitableSettingsGroup("hrs.settings.group.device")]
    [SpoitableSettingsLabelLocalize("hrs.settings.reconnectSeconds.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.reconnectSeconds.description")]
    [SpoitableSettingsRange(1, 300)]
    public int ReconnectSeconds = 5;

    [SpoitableSettingsGroup("hrs.settings.group.recording")]
    [SpoitableSettingsLabelLocalize("hrs.settings.recordingEnabled.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.recordingEnabled.description")]
    public bool RecordingEnabled = true;

    [SpoitableSettingsGroup("hrs.settings.group.osc")]
    [SpoitableSettingsLabelLocalize("hrs.settings.oscPreset.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.oscPreset.description")]
    public HrsOscPreset OscPreset = HrsOscPreset.VRCHR;

    /// <summary>送る心拍と平均を、新しく届いた値へ 1 秒ほどかけて寄せる（<see cref="HeartRateSmoothing"/>）。拍の時刻は変わらない。</summary>
    [SpoitableSettingsGroup("hrs.settings.group.osc")]
    [SpoitableSettingsLabelLocalize("hrs.settings.smoothValues.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.smoothValues.description")]
    public bool SmoothValues = true;

    /// <summary>送る名前はプリセットを選ぶ欄のすぐ下に置く（選んだ方だけが出る）。</summary>
    [SpoitableSettingsGroup("hrs.settings.group.osc")]
    public readonly ParameterSettings Parameters;

    [SpoitableSettingsGroup("hrs.settings.group.osc")]
    public readonly VrcoscParameterSettings VrcoscParameters;

    [SpoitableSettingsGroup("hrs.settings.group.hds")]
    public readonly HdsSettings Hds = new();

    [SpoitableSettingsGroup("hrs.settings.group.broadcast")]
    public readonly BroadcastSettings Broadcast = new();

    [SpoitableSettingsGroup("hrs.settings.group.zones")]
    public readonly ZoneSettings Zones = new();

    /// <summary>名前の 2 つはどのプリセットを選んでいるかを見て出し入れするので、自分を渡して作る（フィールドの初期化子では <c>this</c> を渡せない）。</summary>
    public HrsConfig()
    {
        Parameters = new(this);
        VrcoscParameters = new(this);
    }

    /// <summary>見つかっている心拍計を引く。internal なので設定の項目にならない（reflection は public しか見ない）。スキャンはモジュール側が持つので、後から挿す。</summary>
    internal Func<IReadOnlyList<MonitorIdentity>>? Discovered;

    /// <summary>入れ子の項目の候補も、このクラスが答える。届く名前はメンバー名を繋げたもの（<c>Parameters.Ones</c>）。</summary>
    public IReadOnlyList<string>? SuggestValuesFor(string memberName) =>
        memberName == nameof(Device)
            ? [.. (Discovered?.Invoke() ?? []).Select(d => d.ToSuggestion())]
            : null;
}
