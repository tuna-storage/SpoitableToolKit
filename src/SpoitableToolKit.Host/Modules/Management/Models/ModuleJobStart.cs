namespace SpoitableToolKit.Host.Modules.Management.Models;

/// <summary><see cref="ModuleInstallJobs.Start"/> の答え。<paramref name="Id"/> は出来た（か、もうあった）件、<paramref name="Error"/> は断った訳。</summary>
internal sealed record ModuleJobStart(bool Ok, string? Id, string? Error);
