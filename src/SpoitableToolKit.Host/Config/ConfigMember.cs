using System.Reflection;
using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Config.Attributes;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Host.Config.Attributes;
using SpoitableToolKit.Host.Config.Models;

namespace SpoitableToolKit.Host.Config;

internal sealed class ConfigMember
{
    private readonly FieldInfo? _field;
    private readonly PropertyInfo? _property;

    private ConfigMember(MemberInfo member, Type type, FieldInfo? field, PropertyInfo? property)
    {
        _field = field;
        _property = property;

        Name = member.Name;
        Type = type;
        Key = member.GetCustomAttribute<SpoitableSettingsKeyAttribute>()?.Key ?? member.Name;
        FormerKeys = [.. member.GetCustomAttributes<SpoitableSettingsFormerKeyAttribute>().Select(a => a.Key)];
        IsSection = typeof(ISpoitableSettings).IsAssignableFrom(type);

        // 入れ物のときだけ、型の側にも名前を探しに行く。葉では見ない（ある enum に付けた名前がその型の項目すべてに漏れる）。
        var box = IsSection ? type : null;

        // 翻訳のキーは直書きより優先するが、キーが無ければ直書きに落ちる。
        // メンバー側に片方でも書いてあれば、型の側は見ない（直書きと翻訳キーが別々の場所から来て、どちらが出るのかコードから読めなくなる）。
        var labelKey = member.GetCustomAttribute<SpoitableSettingsLabelLocalizeAttribute>()?.Key;
        var label = member.GetCustomAttribute<SpoitableSettingsLabelAttribute>()?.Label;

        if (labelKey is null && label is null)
        {
            labelKey = box?.GetCustomAttribute<SpoitableSettingsLabelLocalizeAttribute>()?.Key;
            label = box?.GetCustomAttribute<SpoitableSettingsLabelAttribute>()?.Label;
        }

        LabelKey = labelKey;

        // 最後の落とし先はメンバー名（型の名前ではない）。
        Label = label ?? member.Name;

        var descriptionKey = member.GetCustomAttribute<SpoitableSettingsDescriptionLocalizeAttribute>()?.Key;
        var description = member.GetCustomAttribute<SpoitableSettingsDescriptionAttribute>()?.Description;

        if (descriptionKey is null && description is null)
        {
            descriptionKey = box?.GetCustomAttribute<SpoitableSettingsDescriptionLocalizeAttribute>()?.Key;
            description = box?.GetCustomAttribute<SpoitableSettingsDescriptionAttribute>()?.Description;
        }

        DescriptionKey = descriptionKey;
        Description = description;

        Category = box?.GetCustomAttribute<SpoitableSettingsCategoryAttribute>()?.Category
            ?? SpoitableCategory.AlwaysShown;

        Range = member.GetCustomAttribute<SpoitableSettingsRangeAttribute>();
        Hidden = member.GetCustomAttribute<SpoitableSettingsHiddenAttribute>() is not null;

        GroupKey = member.GetCustomAttribute<SpoitableSettingsGroupAttribute>()?.Key;
        Restart = member.GetCustomAttribute<SpoitableSettingsRestartAttribute>() is not null;
        Switches = member.GetCustomAttribute<SpoitableSettingsSwitchesAttribute>() is not null;
        Editor = member.GetCustomAttribute<SettingsEditorAttribute>()?.Name;

        // 値そのものは生で持ち、変わるのは打ち込む欄の見え方だけ（Kind は文字列のまま）。
        Secret = member.GetCustomAttribute<SpoitableSettingsSecretAttribute>() is not null;

        // 見出しは入れ物が切でも残るので、見出しのスイッチは切にしても戻せる。
        IsSectionSwitch = member.Name == nameof(ITogglableSettings.Enabled)
            && typeof(ITogglableSettings).IsAssignableFrom(member.DeclaringType);

        // その入れ物が切でも出す。スイッチが入れ物の中にあると、切にした瞬間にスイッチごと消えて戻せなくなる。
        Toggle = member.GetCustomAttribute<SpoitableSettingsToggleAttribute>() is not null;

        Kind = KindOf(type);
        Choices = ChoicesOf(type);

        if (Kind == ConfigKind.List && ItemTypeOf(type) is { } item)
        {
            ItemKind = KindOf(item);
            Choices = ChoicesOf(item);
        }
    }

    public string Name { get; }

    public string Key { get; }

    public string[] FormerKeys { get; }

    public string Label { get; }

    public string? Description { get; }

    public string? LabelKey { get; }

    public string? DescriptionKey { get; }

    /// <summary>言語が変わると変わるので、描くたびに引き直す。</summary>
    public string LabelFor(ILocalizer text) => LabelKey is { } key ? text[key] : Label;

    public string? DescriptionFor(ILocalizer text) => DescriptionKey is { } key ? text[key] : Description;

    public Type Type { get; }

    public string Kind { get; }

    public string? ItemKind { get; }

    /// <summary>名前で受け渡すので、値を足し引きしても保存が壊れない。</summary>
    public string[]? Choices { get; private set; }

    public SpoitableSettingsRangeAttribute? Range { get; }

    public bool Hidden { get; }

    public string? GroupKey { get; }

    public bool Restart { get; }

    public bool Switches { get; }

    public string? Editor { get; }

    public bool Secret { get; }

    public bool IsSection { get; }

    public SpoitableCategory Category { get; }

    public bool Foldable => Category == SpoitableCategory.Foldable;

    public bool Toggle { get; }

    public bool IsSectionSwitch { get; }

    /// <summary>入れ子だと入れ物の方になるので、読み書きは根の設定ではなくこちらへ通す。</summary>
    public object Owner { get; internal set; } = null!;

    public ConfigSection? Section { get; internal set; }

    public ConfigSection? Self { get; internal set; }

    /// <summary>キーではなくメンバー名で繋げる（<c>SpoitableSettingsKey</c> で名前を変えていても、コード側の名前で届く）。</summary>
    public string NamePath { get; internal set; } = "";

    /// <summary>入れ子だと <c>Server.Url</c> のように繋がる。</summary>
    public string Path { get; internal set; } = "";

    /// <summary>入れ物は読めれば足りる（中身を書き換えるだけで、入れ物そのものは差し替えないため）。</summary>
    public static bool TryCreateSection(MemberInfo member, out ConfigMember bound)
    {
        bound = null!;

        switch (member)
        {
            case FieldInfo field when typeof(ISpoitableSettings).IsAssignableFrom(field.FieldType):
                bound = new ConfigMember(member, field.FieldType, field, null);
                return true;

            case PropertyInfo { CanRead: true } property
                when property.GetIndexParameters().Length == 0
                    && typeof(ISpoitableSettings).IsAssignableFrom(property.PropertyType):
                bound = new ConfigMember(member, property.PropertyType, null, property);
                return true;

            default:
                return false;
        }
    }

    /// <summary>書き込めるメンバーだけを設定として扱う（readonly フィールドや get だけのプロパティは画面から変えられない）。</summary>
    public static bool TryCreate(MemberInfo member, out ConfigMember bound)
    {
        bound = null!;

        switch (member)
        {
            case FieldInfo { IsInitOnly: false, IsLiteral: false } field:
                bound = new ConfigMember(member, field.FieldType, field, null);
                return true;

            case PropertyInfo { CanRead: true, CanWrite: true } property
                when property.GetIndexParameters().Length == 0 && property.SetMethod?.IsPublic == true:
                bound = new ConfigMember(member, property.PropertyType, null, property);
                return true;

            default:
                return false;
        }
    }

    public object? GetValue(object instance) => _field is not null ? _field.GetValue(instance) : _property!.GetValue(instance);

    public void SetValue(object instance, object? value)
    {
        if (_field is not null) _field.SetValue(instance, value);
        else _property!.SetValue(instance, value);
    }

    /// <summary>
    /// 範囲外なら画面に出す理由、そうでなければ <c>null</c>。画面からの書き換えは丸めずに弾く
    /// （丸めると打った値ではないものが保存され、30 のつもりの 300 も 3000 も同じ 30 になって、直った結果が正しいか利用者に判断できない）。
    /// 弾く側に揃えると、モジュールが <c>Reject</c> したときと画面の動きも同じになる。
    /// </summary>
    public string? RangeError(object? value, ILocalizer text)
    {
        if (Range is null || value is not IConvertible) return null;

        var number = Convert.ToDouble(value);
        return number >= Range.Minimum && number <= Range.Maximum
            ? null
            : text.Format("host.config.outOfRange", $"{Range.Minimum:0.##}", $"{Range.Maximum:0.##}");
    }

    /// <summary>
    /// 範囲に収める。ファイルから読むときだけ使う。設定ファイルは直接書き換えられるが、
    /// そこには戻す先が無い（前の値を知らない）ので丸める。
    /// </summary>
    public object? Clamp(object? value)
    {
        if (Range is null || value is not IConvertible) return value;

        var clamped = Math.Clamp(Convert.ToDouble(value), Range.Minimum, Range.Maximum);
        return Convert.ChangeType(clamped, Nullable.GetUnderlyingType(Type) ?? Type);
    }

    private static string KindOf(Type type)
    {
        var actual = Nullable.GetUnderlyingType(type) ?? type;

        if (actual == typeof(bool)) return ConfigKind.Bool;
        if (actual == typeof(string)) return ConfigKind.Text;
        if (actual.IsEnum) return ConfigKind.Choice;

        if (actual == typeof(byte) || actual == typeof(sbyte) || actual == typeof(short) || actual == typeof(ushort)
            || actual == typeof(int) || actual == typeof(uint) || actual == typeof(long) || actual == typeof(ulong))
            return ConfigKind.Integer;

        if (actual == typeof(float) || actual == typeof(double) || actual == typeof(decimal))
            return ConfigKind.Number;

        return ItemTypeOf(actual) is not null ? ConfigKind.List : ConfigKind.Json;
    }

    private static string[]? ChoicesOf(Type type)
    {
        var actual = Nullable.GetUnderlyingType(type) ?? type;
        return actual.IsEnum ? Enum.GetNames(actual) : null;
    }

    /// <summary>配列 / List の中身の型。文字列は列挙できるが一覧ではないので除く。</summary>
    private static Type? ItemTypeOf(Type type)
    {
        if (type == typeof(string)) return null;
        if (type.IsArray) return type.GetElementType();

        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
            ? type.GetGenericArguments()[0]
            : null;
    }
}
