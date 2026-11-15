using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Host.Modules.Build.Sdk;

/// <summary>
/// 使える .NET SDK を<b>探す</b>。探す順: <c>PATH</c> の <c>dotnet</c> → <c>%ProgramFiles%\dotnet</c> → 自分で落としたもの（<c>tools/dotnet</c>）。
/// <c>dotnet --list-sdks</c> に聞いて、版が <see cref="DotnetSdks.Major"/> 以上の SDK があるものを使う。
/// </summary>
/// <param name="logger">SDK の logger（<see cref="DotnetSdks"/> に渡されたもの）。</param>
/// <param name="candidates">探す <c>dotnet</c> の差し替え（<see cref="DotnetSdks.CandidatesOverride"/>）。null ならふだんの探し方。</param>
/// <param name="listSdks"><c>dotnet --list-sdks</c> の答えの差し替え（試験用）。null なら本当に聞く。</param>
internal sealed class DotnetSdkLocator(
    ILogger logger,
    IReadOnlyList<string>? candidates,
    Func<string, CancellationToken, Task<string?>>? listSdks)
{
    // 数字は [0-9] と桁の上限で書く（\d はほかの文字体系の数字にも当たり、桁が多いと int に入らない）。
    private static readonly Regex SdkLine = new(@"^([0-9]{1,4})\.([0-9]{1,4})\.([0-9]{1,6}) \[", RegexOptions.CultureInvariant);

    /// <summary>使える SDK を探す。無ければ null（落とすかを聞く）。</summary>
    /// <param name="ownDotnet">自分で落とした SDK の <c>dotnet.exe</c>（<see cref="DotnetSdkInstaller.OwnDotnet"/>）。<b>差し替えていても、いつも見る</b>。</param>
    /// <param name="cancellationToken">止める口（<c>--list-sdks</c> の子のプロセスも止まる）。</param>
    public async Task<DotnetSdk?> FindAsync(string ownDotnet, CancellationToken cancellationToken)
    {
        foreach (var dotnet in (candidates ?? DefaultCandidates()).Append(ownDotnet).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(dotnet)) continue;

            if (await NewestSdkAsync(dotnet, cancellationToken).ConfigureAwait(false) is { } version)
                return new DotnetSdk(dotnet, version);
        }

        return null;
    }

    private static IReadOnlyList<string> DefaultCandidates() =>
        CandidatesFrom(Environment.GetEnvironmentVariable("PATH") ?? string.Empty, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

    /// <summary>
    /// <c>PATH</c> の <c>dotnet.exe</c>（最初に見つかったもの）と、既定の入れ先。
    /// <b><c>PATH</c> の、絶対パスでない項目（<c>.</c> など）は見ない</b> — いまの作業フォルダにある <c>dotnet.exe</c> を走らせることになる。
    /// </summary>
    internal static IReadOnlyList<string> CandidatesFrom(string path, string programFiles)
    {
        List<string> found = [];
        foreach (var directory in path.Split(Path.PathSeparator))
        {
            var trimmed = directory.Trim().Trim('"');
            if (trimmed.Length == 0) continue;

            try
            {
                if (!Path.IsPathFullyQualified(trimmed)) continue;

                var dotnet = Path.Combine(trimmed, "dotnet.exe");
                if (!File.Exists(dotnet)) continue;

                found.Add(Path.GetFullPath(dotnet));
                break;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // PATH に、道として読めないものが入っている。
            }
        }

        if (programFiles.Length > 0) found.Add(Path.Combine(programFiles, "dotnet", "dotnet.exe"));
        return found;
    }

    /// <summary>その <c>dotnet</c> が持つ SDK のうち、版が <see cref="DotnetSdks.Major"/> 以上でいちばん新しいもの。preview は数えない。</summary>
    public async Task<Version?> NewestSdkAsync(string dotnet, CancellationToken cancellationToken)
    {
        var listing = await (listSdks ?? ListSdksAsync)(dotnet, cancellationToken).ConfigureAwait(false);
        return listing is null ? null : NewestSdk(listing);
    }

    /// <summary>
    /// <c>dotnet --list-sdks</c> の答え（<c>10.0.100 [C:\Program Files\dotnet\sdk]</c> が 1 行に 1 つ）から、使える版を選ぶ。
    /// preview（<c>10.0.100-rc.1 […]</c>）は、版のすぐ後ろが空白でないので当たらない。
    /// </summary>
    internal static Version? NewestSdk(string listing) =>
        listing.Split('\n')
            .Select(line => SdkLine.Match(line.Trim()))
            .Where(match => match.Success)
            .Select(match => new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value)))
            .Where(version => version.Major >= DotnetSdks.Major)
            .OrderDescending()
            .FirstOrDefault();

    private async Task<string?> ListSdksAsync(string dotnet, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(dotnet)
        {
            WorkingDirectory = Path.GetDirectoryName(dotnet)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("--list-sdks");
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        try
        {
            using var process = Process.Start(start);
            if (process is null) return null;

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(30));
            var output = process.StandardOutput.ReadToEndAsync(limit.Token);
            var error = process.StandardError.ReadToEndAsync(limit.Token);
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
                await error.ConfigureAwait(false);
                return process.ExitCode == 0 ? await output.ConfigureAwait(false) : null;
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
                {
                    // もう抜けている・止められない。利用者が止めたのなら、下でそのまま止めたと伝える。
                }

                cancellationToken.ThrowIfCancellationRequested();
                logger.LogWarning("{Dotnet} --list-sdks did not answer in time", dotnet);
                return null;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            logger.LogWarning("{Dotnet} could not be asked for its SDKs: {Reason}", dotnet, ex.Message);
            return null;
        }
    }
}
