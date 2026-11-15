namespace SpoitableToolKit.Host.Modules.Management;

/// <summary><see cref="IProgress{T}"/> を、その場で（呼んだスレッドで）渡す。<c>Progress&lt;T&gt;</c> は UI のスレッドへ回すので使わない。</summary>
internal sealed class DirectProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
