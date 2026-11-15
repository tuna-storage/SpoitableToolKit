using System.Linq.Expressions;
using SpoitableToolKit.Abstractions.Config;

namespace SpoitableToolKit.Abstractions.Events.Config;

/// <summary>
/// 変わった値 1 つ（<see cref="ConfigChangingEventParams{TConfig}.Changes"/>・<see cref="ConfigChangedEventParams{TConfig}.Changes"/>）。
/// <para>
/// どの設定かは式で確かめる（文字列で比べない）: <c>change.Is(c =&gt; c.Hds.Port)</c>。値も一緒に受け取れる:
/// <c>change.Is(c =&gt; c.Device, out var device)</c>。入れ物を書くと、その中の値なら true（<c>change.Is(c =&gt; c.Zones)</c>）。
/// </para>
/// </summary>
public sealed record ConfigChange<TConfig> where TConfig : class, IModuleConfig
{
    private readonly TConfig _values;
    private readonly ConfigSettingNames _settings;

    /// <param name="name">C# の名前を <c>.</c> で繋いだもの（<c>Zones.LightBpm</c>）。</param>
    /// <param name="values">値を読む先（聞いている間は写し、知らせるときは共有の設定）。</param>
    /// <param name="settings">その設定の項目の名前の全部（式が項目を指しているかを確かめる）。</param>
    public ConfigChange(string name, TConfig values, IReadOnlyCollection<string> settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(values);
        Name = name;
        _values = values;
        _settings = new ConfigSettingNames(typeof(TConfig), settings);
    }

    /// <summary>C# の名前を <c>.</c> で繋いだもの（<c>Zones.LightBpm</c>）。記録に書く・並べて見せる用（比べるのは <see cref="Is{TValue}(Expression{Func{TConfig, TValue}})"/> で）。</summary>
    public string Name { get; }

    /// <summary>
    /// この値が <paramref name="setting"/> か（入れ物を書いたら、その中の値か）。
    /// 設定の項目でない式（<c>c =&gt; c.Port + 1</c>・設定画面に出ない要素）は、分かる文で例外にする（黙って false にしない）。
    /// </summary>
    public bool Is<TValue>(Expression<Func<TConfig, TValue>> setting) => _settings.Covers(setting, Name);

    /// <summary>この値が <paramref name="setting"/> なら、その値（聞いている間は新しい値）を <paramref name="value"/> に入れて true。</summary>
    public bool Is<TValue>(Expression<Func<TConfig, TValue>> setting, out TValue value)
    {
        if (!Is(setting))
        {
            value = default!;
            return false;
        }

        value = setting.Compile(preferInterpretation: true)(_values);
        return true;
    }
}
