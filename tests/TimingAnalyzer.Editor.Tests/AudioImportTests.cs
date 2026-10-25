using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class AudioImportTests
{
    [Theory]
    [InlineData("ogg", "vorbis", 2, "192000", null, true)]
    [InlineData("ogg", "vorbis", 2, "160000", null, false)]
    [InlineData("ogg", "opus", 2, "192000", "6", false)]
    [InlineData("mp3", "mp3", 2, "192000", null, false)]
    [InlineData("ogg", "vorbis", 1, "110000", "6", true)]
    [InlineData("ogg", "vorbis", 1, "110000", null, true)]
    [InlineData("ogg", "vorbis", 2, "192000", "5", false)]
    public void EncodingQualityUsesVorbisNominalBitrateRatherThanTheContainersAverage(
        string format, string codec, int channels, string nominal, string? quality, bool expected)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            streams = new[] { new { codec_type = "audio", codec_name = codec, channels, bit_rate = nominal,
                tags = quality is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["tempomap_vorbis_quality"] = quality } } },
            format = new { format_name = format, bit_rate = "12345" }
        });
        Assert.Equal(expected, AudioImport.ParseProbe(json).IsQuality6);
    }
}
