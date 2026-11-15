namespace SpoitableToolKit.Host.Modules;

internal enum ModuleState
{
    /// <summary>インスタンスは出来たが OnInit をまだ通っていない。</summary>
    Initializing,

    /// <summary>読み込みに失敗した。以後のフェーズは呼ばれない。</summary>
    Failed,

    Running,

    Stopped,
}
