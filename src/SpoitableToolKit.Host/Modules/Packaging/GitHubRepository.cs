using System.Text.RegularExpressions;

namespace SpoitableToolKit.Host.Modules.Packaging;

/// <summary>GitHub の repo の URL（<c>https://github.com/&lt;owner&gt;/&lt;repo&gt;</c>）。<b>https の github.com だけ</b>。</summary>
internal sealed record GitHubRepository(string Owner, string Name)
{
    private static readonly Regex Part = new("^[A-Za-z0-9_.-]{1,100}$", RegexOptions.CultureInvariant);

    public override string ToString() => $"{Owner}/{Name}";

    /// <summary>
    /// <c>https://github.com/o/r</c>（後ろの <c>/</c>・<c>.git</c>・<c>/releases…</c> は許す）。それ以外は理由つきで null。
    /// </summary>
    public static GitHubRepository? TryParse(string text, out string? problem)
    {
        problem = null;
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri))
        {
            problem = "not a URL";
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            problem = "only https:// URLs are accepted";
            return null;
        }

        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort || uri.UserInfo.Length > 0)
        {
            problem = "only github.com repositories are accepted";
            return null;
        }

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            problem = "the URL needs both the owner and the repository (https://github.com/<owner>/<repo>)";
            return null;
        }

        var owner = parts[0];
        var name = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        if (!Part.IsMatch(owner) || !Part.IsMatch(name) || owner is "." or ".." || name is "." or "..")
        {
            problem = "the owner or repository name has characters GitHub does not use";
            return null;
        }

        if (parts.Length > 2 && parts[2] != "releases")
        {
            problem = "point at the repository or its releases page";
            return null;
        }

        return new GitHubRepository(owner, name);
    }
}
