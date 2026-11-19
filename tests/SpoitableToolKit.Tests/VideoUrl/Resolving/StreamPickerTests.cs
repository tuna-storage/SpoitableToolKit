using System.Globalization;
using System.Text.Json;
using SpoitableToolKit.Modules.VideoUrl.Resolving;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.VideoUrl.Resolving;

/// <summary>
/// 音声だけの 1 本の選び方。並べる format は、<c>yt-dlp -J</c> の出力の <c>formats</c> から、後段が見る欄だけを残した形。
/// itag と言語の並びは、実物（吹き替えが 24 言語ある動画）と同じ形にしてある。
/// </summary>
public sealed class StreamPickerTests
{
    private static List<ResolvedStream> AudioOnly(params string[] formats)
    {
        using var json = JsonDocument.Parse($$"""{ "formats": [{{string.Join(",", formats)}}] }""");

        List<ResolvedStream> streams = [];
        StreamPicker.AddAudioOnly(StreamPicker.Formats(json.RootElement), streams, new FakeLocalizer());

        return streams;
    }

    /// <summary>音声だけの format。<paramref name="bitrate"/> が null なら <c>abr</c> の欄を書かない。</summary>
    private static string Audio(string id, string ext, double? bitrate, int languagePreference = -1, string protocol = "https") =>
        $$"""
        {
          "format_id": "{{id}}", "ext": "{{ext}}", "protocol": "{{protocol}}",
          "vcodec": "none", "acodec": "{{(ext == "m4a" ? "mp4a.40.2" : "opus")}}",
          {{(bitrate is { } abr ? $"\"abr\": {abr.ToString(CultureInfo.InvariantCulture)}," : "")}}
          "language_preference": {{languagePreference}},
          "url": "https://example.invalid/{{id}}"
        }
        """;

    /// <summary>入れ物ごとにビットレートのいちばん高い 1 本。m4a を先に並べる（webm が先に来ても）。</summary>
    [Fact]
    public void TheBestM4aAndTheBestWebmAreOfferedWithM4aFirst()
    {
        var streams = AudioOnly(
            Audio("249", "webm", 46.234),
            Audio("251", "webm", 128.93),
            Audio("139", "m4a", 48.813),
            Audio("140", "m4a", 129.502));

        Assert.Equal(["https://example.invalid/140", "https://example.invalid/251"], [.. streams.Select(s => s.Url)]);
        Assert.Equal(
            ["videourl.stream.audio.label(129 kbps, m4a)", "videourl.stream.audio.label(128 kbps, webm)"],
            [.. streams.Select(s => s.Label)]);
        Assert.All(streams, s => Assert.Equal(("audio", "videourl.stream.audio.note"), (s.Kind, s.Note)));
    }

    /// <summary>吹き替えより元の言語（<c>language_preference</c> が 10）。吹き替えの方がビットレートが高くても。</summary>
    [Fact]
    public void TheOriginalLanguageWinsOverADubWithAHigherBitrate()
    {
        var streams = AudioOnly(
            Audio("140-13", "m4a", 130.1),
            Audio("140-23", "m4a", 129.475, languagePreference: 10),
            Audio("140-14", "m4a", 131.2));

        Assert.Equal("https://example.invalid/140-23", Assert.Single(streams).Url);
    }

    /// <summary>音量をならした版（<c>-drc</c>）より普通の版。ならした版の方がビットレートが高くても。</summary>
    [Fact]
    public void ThePlainVersionWinsOverTheDrcOne()
    {
        var streams = AudioOnly(
            Audio("140-drc", "m4a", 130.5),
            Audio("140", "m4a", 129.5));

        Assert.Equal("https://example.invalid/140", Assert.Single(streams).Url);
    }

    /// <summary>ならした版しか無ければ、それを出す（避けるのは普通の版があるときだけ）。</summary>
    [Fact]
    public void ADrcVersionIsOfferedWhenItIsTheOnlyOne()
    {
        var streams = AudioOnly(Audio("140-drc", "m4a", 129.5));

        Assert.Equal("https://example.invalid/140-drc", Assert.Single(streams).Url);
    }

    /// <summary>ビットレートの欄が無ければ、数の代わりに「最高音質」と書く。</summary>
    [Fact]
    public void WithoutABitrateTheLabelSaysBest()
    {
        var streams = AudioOnly(Audio("140", "m4a", null));

        Assert.Equal("videourl.stream.audio.label(videourl.stream.audio.best, m4a)", Assert.Single(streams).Label);
    }

    /// <summary>
    /// 音声だけの、素の GET で取れる 1 本しか取らない。映像の入ったもの・音の無いもの・HLS・URL の無いものは取らない。
    /// 音の無いものは、storyboard（protocol が mhtml なので、音を見なくても落ちる）と、素の GET で取れる形の 2 つを置く。
    /// </summary>
    [Fact]
    public void OnlyPlainAudioFormatsWithAUrlAreTaken()
    {
        var streams = AudioOnly(
            """{ "format_id": "18", "ext": "mp4", "protocol": "https", "vcodec": "avc1.42001E", "acodec": "mp4a.40.2", "abr": 96, "url": "https://example.invalid/18" }""",
            """{ "format_id": "sb0", "ext": "mhtml", "protocol": "mhtml", "vcodec": "none", "acodec": "none", "url": "https://example.invalid/sb0" }""",
            """{ "format_id": "silent", "ext": "mp4", "protocol": "https", "vcodec": "none", "acodec": "none", "url": "https://example.invalid/silent" }""",
            Audio("233", "mp4", 48, protocol: "m3u8_native"),
            """{ "format_id": "251", "ext": "webm", "protocol": "https", "vcodec": "none", "acodec": "opus", "abr": 128 }""");

        Assert.Empty(streams);
    }
}
