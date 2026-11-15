namespace SpoitableToolKit.Host.Config.Models;

internal sealed class ConfigValueException(string key, string message) : Exception(message)
{
    public string Key { get; } = key;
}
