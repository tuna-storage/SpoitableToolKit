using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Host.Config;

namespace SpoitableToolKit.Tests.Helpers;

internal static class ConfigTesting
{
    // 聞く順は通さない（通すのは ModuleConfigStore.Apply）。
    public static string[] ApplyNow(this ModuleConfigBinding binding, IEnumerable<KeyValuePair<string, System.Text.Json.Nodes.JsonNode?>> changes, ILocalizer text)
    {
        var changed = binding.Changes(changes, text);
        foreach (var (member, node) in changed) binding.Write(member, node);
        return [.. changed.Select(c => c.Member.NamePath)];
    }

    public static ConfigChangedEventParams<T> Changed<T>(T config, params string[] names) where T : class, IModuleConfig =>
        new(config, names, SettingsOf(config));

    public static IReadOnlyCollection<string> SettingsOf(IModuleConfig config) =>
        [.. new ModuleConfigBinding(config, NullLogger.Instance).Members.Select(m => m.NamePath)];
}
