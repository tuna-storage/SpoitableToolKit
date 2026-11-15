using System.Linq.Expressions;
using SpoitableToolKit.Abstractions.Config;

namespace SpoitableToolKit.Abstractions.Events.Config;

/// <summary>
/// 設定を変えてよいかを聞く出来事（<see cref="IConfigEventListener{TConfig}.OnConfigChanging"/>）。
/// <para>
/// <b>共有の設定はまだ前の値。</b> 新しい値は <see cref="Proposed"/>（入ったらこうなる写し）と <see cref="Changes"/>（変わった値だけ）で見る。
/// 断るのは値ごと（<see cref="Reject{TValue}"/>）。全部を断るなら <c>return EventResult.Stop(理由)</c>。
/// </para>
/// </summary>
public sealed class ConfigChangingEventParams<TConfig> where TConfig : class, IModuleConfig
{
    private readonly ConfigSettingNames _settings;
    private readonly List<ConfigRejection> _rejections = [];

    /// <param name="proposed">入ったらこうなる写し（今の値に、変わった値を重ねたもの）。</param>
    /// <param name="changed">変わった値の名前（C# の名前を <c>.</c> で繋いだもの）。</param>
    /// <param name="settings">その設定の項目の名前の全部。</param>
    public ConfigChangingEventParams(TConfig proposed, IReadOnlyList<string> changed, IReadOnlyCollection<string> settings)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(changed);
        Proposed = proposed;
        _settings = new ConfigSettingNames(typeof(TConfig), settings);
        Changes = [.. changed.Select(name => new ConfigChange<TConfig>(name, proposed, settings))];
    }

    /// <summary>
    /// 入ったらこうなる写し。<b>変わっていない値も入っている</b>（組み合わせを確かめるときはこれを読む）。
    /// 書き換えても何も起きない（写しなので）。設定画面に出ない中身（internal の field など）は写っていない。
    /// </summary>
    public TConfig Proposed { get; }

    /// <summary>変わった値だけ（<c>foreach</c> で回せる）。</summary>
    public IReadOnlyList<ConfigChange<TConfig>> Changes { get; }

    /// <summary>断った値（ホストが読む。1 つの値につき最初の理由だけ）。</summary>
    public IReadOnlyList<ConfigRejection> Rejections => _rejections;

    /// <summary><paramref name="setting"/> が変わったか（入れ物を書いたら、その中のどれかが変わったか）。</summary>
    public bool Changed<TValue>(Expression<Func<TConfig, TValue>> setting) => Changes.Any(change => change.Is(setting));

    /// <summary>
    /// <paramref name="setting"/> を断る（その値は前の値のまま残し、ほかの値は入れる）。入れ物を書いたら、その中の変わった値を全部断る。
    /// 変わっていない値を断っても何も起きない。<paramref name="reason"/> はそのまま設定画面のその欄の下に出る（空は例外）。
    /// </summary>
    public void Reject<TValue>(Expression<Func<TConfig, TValue>> setting, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var path = _settings.Check(setting);

        foreach (var change in Changes)
        {
            if (!ConfigSettingNames.Under(change.Name, path)) continue;
            if (_rejections.Any(r => r.Name == change.Name)) continue;
            _rejections.Add(new ConfigRejection { Name = change.Name, Reason = reason });
        }
    }
}
