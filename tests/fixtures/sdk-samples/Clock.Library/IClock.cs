namespace Clock.Library;

/// <summary>ライブラリが配る型の見本。</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}
