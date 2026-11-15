namespace TestContract;

/// <summary>
/// モジュール間で受け渡す契約の見本。shared/ に置かれ、提供側と消費側が
/// 別々の AssemblyLoadContext から見ても同一の型になることを試験する。
/// </summary>
public interface IGreeter
{
    /// <summary>
    /// この API の名前。渡す側（Id が <c>provider</c> のモジュール）は登録に、使う側は取得に、これをそのまま渡す
    /// （名前は登録するモジュールの Id で始める）。
    /// </summary>
    const string ModuleIdentity = "provider.greeter";

    string Greet();
}
