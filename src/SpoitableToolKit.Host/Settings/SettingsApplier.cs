using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Host.Config.Models;
using SpoitableToolKit.Host.Localization;
using SpoitableToolKit.Host.Ui.Push;

namespace SpoitableToolKit.Host.Settings;

/// <summary>
/// 設定の画面の「適用」と同じ道で値を書く（config.json・<see cref="SpoitableToolKit.Abstractions.Events.Config.IConfigEventListener{TConfig}"/>・全部の面の設定の画面が揃う）。
/// 画面の「適用」（<see cref="Ui.Shell.UiBridge.ApplyConfigChanges"/>）も、画面の外で利用者が選んだ値（閉じるときの問いの答え・トレイのメニュー）もここを通る。
/// <see cref="ModuleConfigStore.Save"/> は listener を通さず画面へも知らせないので、利用者が選んだ値には使わない。
/// </summary>
/// <param name="locales">言語を読み直すときの辞書の置き場（モジュールの分は読み込んだ後でないと分からないので、引くたびに聞く）。</param>
internal sealed class SettingsApplier(
    ModuleConfigStore configs, HostConfig hostConfig, Localizer localizer, Func<IEnumerable<LocaleSource>> locales, UiBroadcast broadcast,
    ILogger logger)
{
    /// <summary>1 つのモジュールの分を当てる。断られた・読めなかったときは <see cref="ConfigRejectedException"/>。画面へはまだ流さない（流すのは <see cref="Announce"/>）。</summary>
    public void Apply(string module, JsonObject changes) => configs.Apply(module, changes, localizer);

    /// <summary>
    /// 当て終わったことを全部の面へ流す。言語が変わっていたら、先に辞書を読み直す（画面はこのあと組み直すので、新しい言語で描かれる）。
    /// 言語が変わったかどうかも伝える。画面はこれを見て、パネルを読み込み直すかを決める。
    /// </summary>
    /// <param name="languageBefore">当てる前の言語。</param>
    /// <returns>言語が変わったか。</returns>
    public bool Announce(UiLanguage languageBefore)
    {
        var languageChanged = hostConfig.Language != languageBefore;
        if (languageChanged) localizer.Load(hostConfig.Language.ToString(), locales());

        broadcast.Push(ConfigChangedPush.Channel, new ConfigChangedPush(languageChanged));
        return languageChanged;
    }

    /// <summary>本体の設定を 1 つだけ書く。断られた・読めなかったら false（前の値のまま。ログに残す）。</summary>
    /// <param name="key">保存名（<see cref="HostConfig"/> のフィールドの名前）。</param>
    public bool SetHost(string key, JsonNode? value)
    {
        var before = hostConfig.Language;
        try
        {
            Apply(HostConfig.Id, new JsonObject { [key] = value });
        }
        catch (Exception ex) when (ex is ConfigRejectedException or ArgumentException)
        {
            logger.LogWarning(ex, "Could not set {Key} to {Value}", key, value?.ToJsonString());
            return false;
        }

        Announce(before);
        return true;
    }
}
