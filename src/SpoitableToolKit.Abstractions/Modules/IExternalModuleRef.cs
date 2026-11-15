namespace SpoitableToolKit.Abstractions.Modules;

/// <summary>
/// 他モジュールが公開したサービスへの参照。
/// <para>
/// <b><see cref="Instance"/> をフィールドに保持してはいけない。</b> 使うたびに取り直すこと。
/// 掴んだままにすると、提供元が落ちたあとも死んだオブジェクトを呼び続けることになり、
/// 提供元の <c>AssemblyLoadContext</c> もアンロードできなくなる。
/// </para>
/// </summary>
public interface IExternalModuleRef<out T> where T : class
{
    /// <summary>提供元が居なくなっていれば null。</summary>
    T? Instance { get; }

    /// <summary>いま使って安全か。</summary>
    bool IsAvailable { get; }
}
