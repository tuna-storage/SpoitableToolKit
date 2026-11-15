using System.Text.RegularExpressions;
using SpoitableToolKit.Abstractions.VrcLog;

namespace SpoitableToolKit.Host.VrcLog.Recognizers;

/// <summary>ワールド名は Joining 行の 1 つ前に来るので、ここで持ち越す。</summary>
internal sealed partial class BehaviourRecognizer
{
    private string? _pendingWorldName;

    [GeneratedRegex(@"^Entering Room: (?<name>.+)$")]
    private static partial Regex EnteringRoomPattern();

    [GeneratedRegex(@"^Joining (?<world>wrld_[0-9a-fA-F-]+):(?<instance>\S+)")]
    private static partial Regex JoiningWorldPattern();

    // 末尾の (usr_...) は VRChat のバージョンによって付いたり付かなかったりするので任意にする。
    [GeneratedRegex(@"^OnPlayerJoined (?<name>.+?)(?: \((?<user>usr_[0-9a-fA-F-]+)\))?$")]
    private static partial Regex PlayerJoinedPattern();

    // 名前の前のスペースを必須にすることで、別イベントの "OnPlayerLeftRoom" を踏まない。
    [GeneratedRegex(@"^OnPlayerLeft (?<name>.+?)(?: \((?<user>usr_[0-9a-fA-F-]+)\))?$")]
    private static partial Regex PlayerLeftPattern();

    // "59273~private(usr_x)~region(jp)" の "private(usr_x)" のような区画。
    [GeneratedRegex(@"^(?<key>[A-Za-z]+)(?:\((?<value>[^)]*)\))?$")]
    private static partial Regex InstanceSegmentPattern();

    public Func<VrcLogLine, VrcLogEventParams>? Recognize(string rest)
    {
        if (EnteringRoomPattern().Match(rest) is { Success: true } entering)
        {
            _pendingWorldName = entering.Groups["name"].Value;
            return null;
        }

        if (JoiningWorldPattern().Match(rest) is { Success: true } joining)
        {
            var worldId = joining.Groups["world"].Value;
            var rawInstance = joining.Groups["instance"].Value;
            var worldName = _pendingWorldName;
            var parsed = ParseInstance(rawInstance);

            _pendingWorldName = null;

            return line => new WorldJoined
            {
                Line = line,
                WorldId = worldId,
                InstanceName = parsed.Name,
                WorldName = worldName,
                AccessType = parsed.AccessType,
                OwnerId = parsed.OwnerId,
                Region = parsed.Region,
                RawInstance = rawInstance,
            };
        }

        if (PlayerJoinedPattern().Match(rest) is { Success: true } joined)
        {
            var name = joined.Groups["name"].Value;
            var userId = joined.Groups["user"].Success ? joined.Groups["user"].Value : null;
            return line => new PlayerJoined { Line = line, DisplayName = name, UserId = userId };
        }

        if (PlayerLeftPattern().Match(rest) is { Success: true } left)
        {
            var name = left.Groups["name"].Value;
            var userId = left.Groups["user"].Success ? left.Groups["user"].Value : null;
            return line => new PlayerLeft { Line = line, DisplayName = name, UserId = userId };
        }

        return null;
    }

    /// <summary>"59273~friends(usr_x)~region(jp)" を分解する。未知の区画は無視する。</summary>
    internal static (string Name, string? AccessType, string? OwnerId, string? Region) ParseInstance(string raw)
    {
        var segments = raw.Split('~');
        var name = segments[0];
        string? accessType = null;
        string? ownerId = null;
        string? region = null;

        foreach (var segment in segments.Skip(1))
        {
            var match = InstanceSegmentPattern().Match(segment);
            if (!match.Success) continue;

            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Success ? match.Groups["value"].Value : null;

            if (key == "region")
            {
                region = value;
                continue;
            }

            // public 以外はアクセス種別が owner 付きの区画として現れる
            // (private / friends / hidden / group / groupAccessType など)。
            accessType ??= key;
            if (ownerId is null && value is { Length: > 0 } && value.Contains('_'))
                ownerId = value;
        }

        return (name, accessType, ownerId, region);
    }
}
