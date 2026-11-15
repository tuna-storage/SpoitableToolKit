using System.Reflection;
using SpoitableToolKit.Host.Ui.Browser;
using SpoitableToolKit.Host.Ui.Shell;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Shell;

/// <summary>
/// <see cref="UiBridge"/> に token を要求しない口が生えていないことを見る。
/// bind はブラウザ単位なので、モジュールの iframe からも <c>stkHost</c> は掴める。呼べないようにしているのは各メソッドの token だけで、1 つでも忘れるとそこだけ通る。
/// 忘れは新しいメソッドを足したときに起きるので、ここで見張る。
/// 公開プロパティも見る（CefSharp は公開メンバを JS へ出すので、<c>public string Token { get; }</c> のようなものが生えると仕組みごと無意味になる）。
/// </summary>
public sealed class UiBridgeGuardTests
{
    /// <summary>bind の対象になる面。<c>object</c> から継承したものは CefSharp も出さない。</summary>
    private static MethodInfo[] Surface =>
    [
        .. typeof(UiBridge)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName),
    ];

    [Fact]
    public void EveryPublicMethodTakesTheTokenFirst()
    {
        var surface = Surface;

        // 数え忘れの保険。0 件なら反射の条件が間違っていて、この試験は何も見ていない。
        Assert.NotEmpty(surface);

        var unguarded = surface
            .Where(m => m.GetParameters() is not [{ ParameterType: var first, Name: "token" }, ..]
                        || first != typeof(string))
            .Select(m => m.Name)
            .ToArray();

        Assert.Empty(unguarded);
    }

    [Fact]
    public void NoPublicPropertiesAreExposed() =>
        Assert.Empty(typeof(UiBridge)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(p => p.Name));

    [Fact]
    public void TheTokenChangesEveryRun() =>
        Assert.NotEqual(new UiToken().Value, new UiToken().Value);

    // 推測できる長さ（16 進 64 文字）。16 進なのは JS へ埋め込むときに引用符を気にしないため。
    [Fact]
    public void TheTokenIsLongEnoughAndHexadecimal()
    {
        var token = new UiToken().Value;

        Assert.Equal(64, token.Length);
        Assert.All(token, c => Assert.True(char.IsAsciiHexDigitUpper(c), $"16 進ではない: {c}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public void AWrongTokenIsRejected(string? candidate) =>
        Assert.False(new UiToken().Matches(candidate));

    [Fact]
    public void TheRightTokenIsAccepted()
    {
        var token = new UiToken();

        Assert.True(token.Matches(token.Value));
    }

    // 前方一致で通すと、長さで早く返す実装は 1 文字ずつ伸ばして当てられる。
    [Fact]
    public void AMatchingPrefixAloneIsRejected()
    {
        var token = new UiToken();

        Assert.False(token.Matches(token.Value[..32]));
        Assert.False(token.Matches(token.Value + "0"));
    }
}
