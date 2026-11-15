using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Host.Ui.Licenses;

internal sealed class LicenseTexts(string directory, ILogger logger)
{
    public const string AppFile = "LICENSE";

    public const string ThirdPartyFile = "THIRD-PARTY-NOTICES.md";

    public LicensesView Read() => new(ReadFile(AppFile), ReadFile(ThirdPartyFile));

    private string? ReadFile(string name)
    {
        var path = Path.Combine(directory, name);
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("The license file {Path} could not be read: {Message}", path, ex.Message);
            return null;
        }
    }
}
