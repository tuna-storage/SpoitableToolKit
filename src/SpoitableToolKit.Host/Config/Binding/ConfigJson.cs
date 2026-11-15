using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Config.Attributes;
using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Abstractions.Localization;

namespace SpoitableToolKit.Host.Config.Binding;

internal static class ConfigJson
{
    /// <summary>enum は名前で受け渡す。数値だと、値を足し引きしたときに保存済みの設定が別物になる。</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    /// <summary>
    /// 保存済みの値を流し込む。
    /// <b>今の名前 → 昔の名前の順に見る</b>（<see cref="SpoitableSettingsFormerKeyAttribute"/>）。
    /// 読めなかったものは既定値のまま残す（1 つ壊れていても他を巻き添えにしない）。
    /// </summary>
    public static void Load(IReadOnlyList<ConfigMember> members, JsonObject saved, ILogger logger)
    {
        foreach (var member in members)
        {
            // 入れ子はここで辿る。平らな設定では saved がそのまま返る。
            if (Container(saved, member.Section) is not { } box) continue;

            var node = box[member.Key];

            // 今の名前が無いときだけ昔の名前を見る。両方あるなら今の方が正。
            if (node is null)
                foreach (var former in member.FormerKeys)
                    if (box[former] is { } old) { node = old; break; }

            if (node is null) continue;

            try
            {
                var value = node.Deserialize(member.Type, Options);

                // ファイルは直接書き換えられる。ここには戻す先が無いので丸めるしかないが、
                // 画面も出ないので黙ると誰も気付かない。
                if (member.RangeError(value, NoText.Instance) is { } outOfRange)
                {
                    logger.LogWarning("Setting {Key} {Reason}; clamped to range", member.Path, outOfRange);
                    value = member.Clamp(value);
                }

                member.SetValue(member.Owner, value);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
            {
                logger.LogWarning(ex, "Cannot read setting {Key}; keeping the default", member.Path);
            }
        }
    }

    /// <summary>いまの値を書き出す。<b>書くのは常に今の名前</b>なので、次の保存で移行が終わる。</summary>
    public static JsonObject Save(IReadOnlyList<ConfigMember> members)
    {
        var saved = new JsonObject();
        foreach (var member in members)
            Ensure(saved, member.Section)[member.Key] =
                JsonSerializer.SerializeToNode(member.GetValue(member.Owner), member.Type, Options);

        return saved;
    }

    /// <summary>
    /// 同じ値か。<b>配列は中身で見る</b> — <c>Equals</c> だと参照比較になり、
    /// 同じ内容を送り直しただけで「変わった」ことになって <see cref="IConfigEventListener{TConfig}"/> が走る。
    /// </summary>
    public static bool SameValue(object? current, object? next, Type type) =>
        JsonSerializer.Serialize(current, type, Options) == JsonSerializer.Serialize(next, type, Options);

    private static JsonObject? Container(JsonObject saved, ConfigSection? section) =>
        section is null
            ? saved
            : Container(saved, section.Parent) is { } parent
                ? parent[section.Member.Key] as JsonObject
                : null;

    private static JsonObject Ensure(JsonObject saved, ConfigSection? section)
    {
        if (section is null) return saved;

        var parent = Ensure(saved, section.Parent);
        if (parent[section.Member.Key] is JsonObject box) return box;

        var made = new JsonObject();
        parent[section.Member.Key] = made;
        return made;
    }

    /// <summary>読み込みのときだけ使う「訳さない」文言。ログにしか出ないので、キーと材料がそのまま見えている方が探しやすい。</summary>
    private sealed class NoText : ILocalizer
    {
        public static readonly NoText Instance = new();

        public string Language => "-";

        public string this[string key] => key;

        public string Format(string key, params object?[] arguments) =>
            $"{key} ({string.Join(", ", arguments)})";
    }
}
