using System.Diagnostics;
using System.Text;
using SpoitableToolKit.Modules.Hrs.Devices.Hds;
using Xunit;

namespace SpoitableToolKit.Tests.Hrs.Devices;

/// <summary>
/// ファイアウォールを見る・直す PowerShell の script。実行ファイルのパスは外から来る字なので、script の字には混ぜない。
/// PowerShell は <c>'</c> のほかに <c>‘ ’ ‚ ‛</c> も単引用符として読み、<c>'</c> を重ねて逃がすだけだと
/// 「Bob’s」のようなフォルダで文字列が閉じて、残りがコマンドとして走る（直す方は管理者で）。
/// <para>ファイアウォールには触らない。PowerShell は script を読ませる（構文木を作る）のと、字を戻させるのにだけ使う。</para>
/// </summary>
public sealed class HdsFirewallScriptTests
{
    /// <summary>引用符を 4 種類とも含み、閉じたら走るコマンドを仕込んだパス。</summary>
    private const string QuotedPath = @"C:\Users\Bob’s ‘Games‚ ‛x' ; Write-Output pwned ; ’\SpoitableToolKit.Host.exe";

    [Fact]
    public void ThePathNeverAppearsInTheScriptText()
    {
        foreach (var script in new[] { HdsFirewall.CheckScript(3476, QuotedPath), HdsFirewall.FixScript(3476, QuotedPath) })
        {
            Assert.DoesNotContain("Bob", script);
            Assert.DoesNotContain("pwned", script);
            foreach (var quote in "‘’‚‛") Assert.DoesNotContain(quote, script);
            Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes(QuotedPath)), script);
        }
    }

    /// <summary>
    /// PowerShell 自身に読ませる。どちらの script も構文の誤りが無く、並ぶコマンドは書いたものだけ（仕込んだ Write-Output は無い）。
    /// script に入れた字は、PowerShell の中で元のパスに戻る。引数は -EncodedCommand で渡す（本物と同じ道）。
    /// </summary>
    [Fact]
    public async Task PowerShellReadsBothScriptsAsWrittenAndGetsThePathBack()
    {
        var reader = $$"""
            [Console]::OutputEncoding = [Text.Encoding]::UTF8
            foreach ($name in 'STK_CHECK', 'STK_FIX') {
              $tokens = $null
              $errors = $null
              $ast = [System.Management.Automation.Language.Parser]::ParseInput((Get-Item "env:$name").Value, [ref]$tokens, [ref]$errors)
              "$name errors=$($errors.Count)"
              $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { "$name command=$($_.GetCommandName())" }
            }
            'path=' + {{HdsFirewall.Literal(QuotedPath)}}
            """;

        var start = new ProcessStartInfo("powershell", HdsFirewall.Arguments(reader))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        start.Environment["STK_CHECK"] = HdsFirewall.CheckScript(3476, QuotedPath);
        start.Environment["STK_FIX"] = HdsFirewall.FixScript(3476, QuotedPath);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("powershell を起動できなかった");
        var errors = process.StandardError.ReadToEndAsync();
        var lines = (await process.StandardOutput.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        await process.WaitForExitAsync();

        var all = string.Join(Environment.NewLine, lines) + Environment.NewLine + await errors;
        Assert.Contains("STK_CHECK errors=0", lines);
        Assert.Contains("STK_FIX errors=0", lines);

        string[] check = [.. lines.Where(l => l.StartsWith("STK_CHECK command=", StringComparison.Ordinal)).Select(l => l["STK_CHECK command=".Length..]).Distinct().Order(StringComparer.Ordinal)];
        string[] fix = [.. lines.Where(l => l.StartsWith("STK_FIX command=", StringComparison.Ordinal)).Select(l => l["STK_FIX command=".Length..]).Distinct().Order(StringComparer.Ordinal)];
        Assert.True(check.SequenceEqual(["ForEach-Object", "Get-NetFirewallApplicationFilter", "Get-NetFirewallRule", "Where-Object"]), all);
        Assert.True(fix.SequenceEqual(["Get-NetFirewallApplicationFilter", "Get-NetFirewallRule", "New-NetFirewallRule", "Out-Null", "Remove-NetFirewallRule", "Where-Object"]), all);

        Assert.Contains("path=" + QuotedPath, lines);
    }
}
