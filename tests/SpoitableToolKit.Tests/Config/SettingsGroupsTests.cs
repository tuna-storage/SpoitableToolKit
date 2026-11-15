using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Modules.Hrs.Config;
using SpoitableToolKit.Modules.VideoUrl.Config;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.Config;

/// <summary>
/// 本物の 3 つの設定の束ね。本体は束ね 1 つが設定画面の左の一覧の 1 ページ、モジュールは束ねが右の目次になる。
/// 画面はこの並び（groupKey の出てくる順）のまま描くので、並びも見る。
/// </summary>
public sealed class SettingsGroupsTests
{
    private static object[] Describe(IModuleConfig config) =>
        new ModuleConfigBinding(config, NullLogger.Instance).Describe(new FakeLocalizer());

    private static string? Read(object item, string property) =>
        item.GetType().GetProperty(property)?.GetValue(item)?.ToString();

    /// <summary>入れ子の中の項目を除いた、いちばん上の並び（名前:束ねのキー）。</summary>
    private static string[] TopLevel(IModuleConfig config) =>
        [.. Describe(config).Where(item => Read(item, "depth") == "0").Select(item => $"{Read(item, "name")}:{Read(item, "groupKey")}")];

    [Fact]
    public void TheHostCategoriesComeInTheirScreenOrder()
    {
        var keys = Describe(new HostConfig()).Where(item => Read(item, "depth") == "0").Select(item => Read(item, "groupKey") ?? "").Distinct().ToArray();

        Assert.Equal(
            [
                "host.settings.group.display", "host.settings.group.log", "host.settings.group.notify", "host.settings.group.osc",
                "host.settings.group.vr", "host.settings.group.modules", "host.settings.group.updates",
            ],
            keys);
    }

    /// <summary>束ねは続いている間だけ 1 つ。同じ束ねが離れて 2 度出ると、画面では同じ見出しが 2 つ並ぶ。</summary>
    [Fact]
    public void EachHostCategoryIsOneRun()
    {
        var keys = Describe(new HostConfig()).Where(item => Read(item, "depth") == "0").Select(item => Read(item, "groupKey")).ToArray();
        var runs = keys.Where((key, i) => i == 0 || keys[i - 1] != key).ToArray();

        Assert.Equal(runs.Distinct().Count(), runs.Length);
    }

    [Fact]
    public void TheHeartRateSettingsAreSplitIntoTheirSubcategories()
    {
        var top = TopLevel(new HrsConfig());

        Assert.Equal(
            [
                "Device:hrs.settings.group.device", "NoSignalSeconds:hrs.settings.group.device", "ReconnectSeconds:hrs.settings.group.device",
                "RecordingEnabled:hrs.settings.group.recording",
                "OscPreset:hrs.settings.group.osc", "SmoothValues:hrs.settings.group.osc",
                "Parameters:hrs.settings.group.osc", "VrcoscParameters:hrs.settings.group.osc",
                "Hds:hrs.settings.group.hds",
                "Broadcast:hrs.settings.group.broadcast",
                "Zones:hrs.settings.group.zones",
            ],
            top);
    }

    /// <summary>入れ物に付けた束ねは中の項目には付かない（画面は入れ物と同じ囲みに入れる）。</summary>
    [Fact]
    public void TheItemsInsideASectionCarryNoGroupOfTheirOwn()
    {
        var inner = Describe(new HrsConfig()).Single(item => Read(item, "key") == "Parameters.Ones");

        Assert.Null(Read(inner, "groupKey"));
        Assert.Null(Read(inner, "group"));
    }

    [Fact]
    public void TheVideoUrlSettingsAreSplitIntoTheirSubcategories()
    {
        var top = TopLevel(new VideoUrlConfig());

        Assert.Equal(
            ["Server:videourl.settings.group.server", "Clients:videourl.settings.group.clients", "AskOnFailure:videourl.settings.group.confirm"],
            top);
    }

    /// <summary>
    /// 並びを入れ替える前の順で書かれた config.json（前の版が保存したもの）も、そのまま読める。JSON は名前で読むので、並びは効かない。
    /// 既定と違う値を全部の束ねに置いて、どれも落ちないことを見る。
    /// </summary>
    [Fact]
    public void AHeartRateConfigSavedInTheOldOrderStillLoads()
    {
        var saved = (JsonObject)JsonNode.Parse(
            """
            {
              "Device": "Polar H10 [A1:B2:C3:D4:E5:F6]",
              "NoSignalSeconds": 20,
              "ReconnectSeconds": 9,
              "RecordingEnabled": false,
              "OscPreset": "VRCOSC",
              "SmoothValues": false,
              "Hds": { "Enabled": true, "Port": 4000, "NoSignalSeconds": 90 },
              "Broadcast": { "Enabled": true, "Port": 9200 },
              "Zones": { "LightBpm": 95, "ModerateBpm": 125, "HardBpm": 150 },
              "Parameters": { "Ones": "e2eOnes", "HeartRate": "e2eHR" },
              "VrcoscParameters": { "Value": "e2e/Value", "Hundreds": "e2e/Hundreds" }
            }
            """)!;

        var config = new HrsConfig();
        new ModuleConfigBinding(config, NullLogger.Instance).Load(saved, NullLogger.Instance);

        Assert.Equal("Polar H10 [A1:B2:C3:D4:E5:F6]", config.Device);
        Assert.Equal(20, config.NoSignalSeconds);
        Assert.Equal(9, config.ReconnectSeconds);
        Assert.False(config.RecordingEnabled);
        Assert.Equal(HrsOscPreset.VRCOSC, config.OscPreset);
        Assert.False(config.SmoothValues);
        Assert.True(config.Hds.Enabled);
        Assert.Equal(4000, config.Hds.Port);
        Assert.Equal(90, config.Hds.NoSignalSeconds);
        Assert.True(config.Broadcast.Enabled);
        Assert.Equal(9200, config.Broadcast.Port);
        Assert.Equal(95, config.Zones.LightBpm);
        Assert.Equal(125, config.Zones.ModerateBpm);
        Assert.Equal(150, config.Zones.HardBpm);
        Assert.Equal("e2eOnes", config.Parameters.Ones);
        Assert.Equal("e2eHR", config.Parameters.HeartRate);
        Assert.Equal("e2e/Value", config.VrcoscParameters.Value);
        Assert.Equal("e2e/Hundreds", config.VrcoscParameters.Hundreds);
    }
}
