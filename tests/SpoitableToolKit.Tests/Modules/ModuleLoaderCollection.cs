using Xunit;

namespace SpoitableToolKit.Tests.Modules;

/// <summary>
/// ローダの試験は直列に流す。<c>shared/</c> のアセンブリは既定コンテキストへ載り、解決フックも一度挿すと外れない（プロセス全体で共有される）。
/// </summary>
[CollectionDefinition("module-loader", DisableParallelization = true)]
public sealed class ModuleLoaderCollection;
