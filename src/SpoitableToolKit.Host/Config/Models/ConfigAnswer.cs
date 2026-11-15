using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Events.Config;

namespace SpoitableToolKit.Host.Config.Models;

internal sealed record ConfigAnswer(EventResult Result, IReadOnlyList<ConfigRejection> Rejections);
