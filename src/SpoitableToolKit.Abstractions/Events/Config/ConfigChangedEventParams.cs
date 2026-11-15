using System.Linq.Expressions;
using SpoitableToolKit.Abstractions.Config;

namespace SpoitableToolKit.Abstractions.Events.Config;

/// <summary>
/// 設定が変わった出来事（<see cref="IConfigEventListener{TConfig}.OnConfigChanged"/>）。保存も済んでいる。
/// <b>並ぶのは実際に入った値だけ</b>（断られた値は入らない）。
/// </summary>
public sealed record ConfigChangedEventParams<TConfig> where TConfig : class, IModuleConfig
{
    private readonly ConfigSettingNames _settings;

    /// <param name="config">共有の設定（新しい値が入っている）。</param>
    /// <param name="changed">入った値の名前（C# の名前を <c>.</c> で繋いだもの）。</param>
    /// <param name="settings">その設定の項目の名前の全部。</param>
    public ConfigChangedEventParams(TConfig config, IReadOnlyList<string> changed, IReadOnlyCollection<string> settings)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(changed);
        Config = config;
        _settings = new ConfigSettingNames(typeof(TConfig), settings);
        Changes = [.. changed.Select(name => new ConfigChange<TConfig>(name, config, settings))];
    }

    /// <summary>共有の設定（自分の <see cref="ISpoitableModule.Config"/> と同じもの。新しい値が入っている）。</summary>
    public TConfig Config { get; }

    /// <summary>入った値だけ。</summary>
    public IReadOnlyList<ConfigChange<TConfig>> Changes { get; }

    /// <summary><paramref name="setting"/> が変わったか（入れ物を書いたら、その中のどれかが変わったか）。</summary>
    public bool Changed<TValue>(Expression<Func<TConfig, TValue>> setting) => Changes.Any(change => change.Is(setting));
}
