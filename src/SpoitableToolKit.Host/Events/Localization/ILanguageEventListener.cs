using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Host.Localization;

namespace SpoitableToolKit.Host.Events.Localization;

/// <summary>Host の中だけ。既定の実装は置かない（実装し忘れを build で見つける）。</summary>
internal interface ILanguageEventListener : IEventListener
{
    void OnLanguageLoaded(LanguageLoadedEventParams e);
}
