using System.Text.Json;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Ui.LogView;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.LogView;

/// <summary>
/// ここが唯一の組み立て場所なので、落とした項目はどの経路（push / 遡り）からも出てこない。
/// id を残すのが要件: 表示名もワールド名も変わるが id は変わらないので、後から辿れるのは id の方。
/// </summary>
public sealed class LogRowTests
{
    private static VrcLogLine Line(string message) => new()
    {
        Timestamp = new DateTimeOffset(2026, 9, 2, 19, 39, 23, TimeSpan.FromHours(9)),
        Level = VrcLogLevel.Debug,
        Tag = "Behaviour",
        Message = message,
        SourceFile = "output_log_2026-09-02_19-39-01.txt",
    };

    /// <summary>開いたときに出る項目を <c>ラベル → 値</c> で引く。</summary>
    private static Dictionary<string, string> Fields(object row)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(row, row.GetType()));

        return !json.RootElement.TryGetProperty("fields", out var fields)
            ? []
            : fields.EnumerateArray().ToDictionary(
                f => f.GetProperty("label").GetString()!,
                f => f.GetProperty("value").GetString()!);
    }

    private static string Title(object row)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(row, row.GetType()));
        return json.RootElement.GetProperty("title").GetString()!;
    }

    [Fact]
    public void JoinAndLeaveCarryTheUserId()
    {
        const string user = "usr_00000000-1111-2222-3333-444444444444";

        var joined = LogRow.From(1, new PlayerJoined
        {
            Line = Line($"[Behaviour] OnPlayerJoined Someone ({user})"),
            DisplayName = "Someone",
            UserId = user,
        });

        var left = LogRow.From(2, new PlayerLeft
        {
            Line = Line($"[Behaviour] OnPlayerLeft Someone ({user})"),
            DisplayName = "Someone",
            UserId = user,
        });

        Assert.Equal(user, Fields(joined)["host.log.field.userId"]);
        Assert.Equal(user, Fields(left)["host.log.field.userId"]);

        // 本文は表示名のまま。id は開いたときだけ。
        Assert.Equal("Someone", Title(joined));
    }

    /// <summary>
    /// <b>VRChat のバージョンによっては行に id が無い。</b>
    /// 空のまま渡すと「ユーザー ID: 」という中身の無い行が並ぶので、ここで落とす。
    /// </summary>
    [Fact]
    public void WithoutAUserIdTheFieldIsLeftOut()
    {
        var row = LogRow.From(1, new PlayerJoined
        {
            Line = Line("[Behaviour] OnPlayerJoined Someone"),
            DisplayName = "Someone",
            UserId = null,
        });

        Assert.Empty(Fields(row));
    }

    /// <summary>名前が取れるとワールド id は本文から消える（<c>title</c> が名前に倒れる）。それでも id は残すので、名前がある行でこそ確かめる。</summary>
    [Fact]
    public void TheWorldIdStaysEvenWhenTheNameIsKnown()
    {
        const string world = "wrld_98be327c-970a-4e0a-94f6-2277f5144032";
        const string owner = "usr_00000000-1111-2222-3333-444444444444";

        var row = LogRow.From(1, new WorldJoined
        {
            Line = Line($"[Behaviour] Joining {world}:75918~private({owner})~region(jp)"),
            WorldId = world,
            WorldName = "Shelter",
            InstanceName = "75918",
            AccessType = "private",
            OwnerId = owner,
            Region = "jp",
            RawInstance = $"75918~private({owner})~region(jp)",
        });

        Assert.Equal("Shelter", Title(row));
        Assert.Equal(world, Fields(row)["host.log.field.worldId"]);
        Assert.Equal(owner, Fields(row)["host.log.field.owner"]);
    }

    /// <summary>public のインスタンスにはオーナーが付かない。項目ごと出さない。</summary>
    [Fact]
    public void AnInstanceWithoutAnOwnerLeavesTheFieldOut()
    {
        const string world = "wrld_98be327c-970a-4e0a-94f6-2277f5144032";

        var row = LogRow.From(1, new WorldJoined
        {
            Line = Line($"[Behaviour] Joining {world}:12345~region(jp)"),
            WorldId = world,
            InstanceName = "12345",
            AccessType = "public",
            OwnerId = null,
            Region = "jp",
            RawInstance = "12345~region(jp)",
        });

        Assert.Equal([world], Fields(row).Values);
    }
}
