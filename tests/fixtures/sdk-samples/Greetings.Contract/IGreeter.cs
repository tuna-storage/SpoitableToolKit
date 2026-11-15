namespace Greetings.Contract;

/// <summary>挨拶を返す API。渡す側は Id が <c>greeter</c> のモジュール。</summary>
public interface IGreeter
{
    /// <summary>
    /// この API の名前。渡す側は登録に、使う側は取得に、これをそのまま渡す（名前は登録するモジュールの Id で始める）。
    /// </summary>
    const string ModuleIdentity = "greeter.api";

    string Greet(string name);
}
