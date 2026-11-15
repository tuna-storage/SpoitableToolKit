using System.Security.Cryptography;
using System.Text;
using SpoitableToolKit.Host.Ui.Desktop;
using SpoitableToolKit.Host.Ui.Shell;

namespace SpoitableToolKit.Host.Ui.Browser;

/// <summary>
/// シェルのフレームにだけ渡す token。<see cref="UiBridge"/> の全メソッドがこれを要求する。
/// bind はブラウザ単位でフレーム単位ではないので、<c>stkHost</c> を <c>Register</c> するとモジュールの iframe からも <c>CefSharp.BindObjectAsync('stkHost')</c> が通る。
/// bind を止める手が無いので、bind できても呼べない形にする。
/// 成立する根拠は同一生成元ポリシー: モジュールのページは <c>spoitable://&lt;Id&gt;</c> という別 origin で動くので、シェル（<c>spoitable://ui</c>）の JS 変数を読めない（独自スキームでも真の tuple origin になる）。
/// プロセスに 1 つでよい。VR オーバーレイ（OSR）のブラウザにも、同じ token を同じやり方（<see cref="ShellTokenInjector"/>）でそのシェルへ渡すこと。
/// 渡し忘れると、そのサーフェスからはホストの機能が一切呼べなくなる（<c>boot()</c> が例外で止まるので画面に出る）。
/// </summary>
internal sealed class UiToken
{
    /// <summary>16 進で持つ。JS へ埋め込むときに引用符の心配が要らず、ログに出しても改行やタブが混ざらないため。</summary>
    public string Value { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>合っているか。長さと内容で早く返さない（総当たりの相手が同一プロセスの JS なので、<c>performance.now()</c> で差を測れる位置に居る）。</summary>
    public bool Matches(string? candidate)
    {
        if (candidate is null) return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(candidate), Encoding.ASCII.GetBytes(Value));
    }
}
