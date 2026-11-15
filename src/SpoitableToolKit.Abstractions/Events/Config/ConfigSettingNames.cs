using System.Linq.Expressions;
using System.Reflection;

namespace SpoitableToolKit.Abstractions.Events.Config;

/// <summary>設定の項目の名前の全部と、式から項目の名前を読む口（式が項目を指していなければ、分かる文で断る）。</summary>
internal sealed class ConfigSettingNames(Type config, IReadOnlyCollection<string> settings)
{
    /// <summary><paramref name="name"/> が <paramref name="path"/> そのものか、その入れ物の中か。</summary>
    public static bool Under(string name, string path) =>
        name == path || (name.Length > path.Length && name.StartsWith(path, StringComparison.Ordinal) && name[path.Length] == '.');

    public bool Covers(LambdaExpression setting, string name) => Under(name, Check(setting));

    /// <summary>式が指す項目（か入れ物）の名前。項目でも入れ物でもなければ例外。</summary>
    public string Check(LambdaExpression setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        var path = PathOf(setting);
        if (settings.Any(name => Under(name, path))) return path;

        throw new ArgumentException(
            $"'{path}' is not a setting of {config.Name}: name a setting the settings screen shows (a public field or property that can be written), " +
            $"or a section that holds some, like c => c.{settings.FirstOrDefault() ?? "Port"}.",
            nameof(setting));
    }

    /// <summary><c>c =&gt; c.Zones.LightBpm</c> → <c>Zones.LightBpm</c>。項目を辿るだけの式でなければ例外。</summary>
    private string PathOf(LambdaExpression setting)
    {
        var body = Unwrap(setting.Body);
        Stack<string> names = [];

        while (body is MemberExpression { Member: FieldInfo or PropertyInfo } member)
        {
            names.Push(member.Member.Name);
            body = member.Expression is { } inner ? Unwrap(inner) : null;
        }

        if (names.Count == 0 || body != setting.Parameters[0])
            throw new ArgumentException(
                $"'{setting}' does not name a setting of {config.Name}: write only the path to it from the parameter, like c => c.Hds.Port " +
                "(not a calculation, a method call or a value from somewhere else).",
                nameof(setting));

        return string.Join('.', names);
    }

    /// <summary>値型を object などで受けたときに入る変換を剥がす。</summary>
    private static Expression Unwrap(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
            expression = convert.Operand;
        return expression;
    }
}
