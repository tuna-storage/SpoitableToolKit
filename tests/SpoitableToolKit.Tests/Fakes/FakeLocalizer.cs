using SpoitableToolKit.Abstractions.Localization;

namespace SpoitableToolKit.Tests.Fakes;

/// <summary>キーをそのまま返す。試験は訳ではなくキーで書ける（本物も、訳が無いキーはそのまま返す）。</summary>
internal sealed class FakeLocalizer : ILocalizer
{
    public string Language => "ja";

    public string this[string key] => key;

    /// <summary>キーと材料をそのまま並べる。どの項目が理由になったかを試験から見たいため。</summary>
    public string Format(string key, params object?[] arguments) =>
        arguments.Length == 0 ? key : $"{key}({string.Join(", ", arguments)})";
}
