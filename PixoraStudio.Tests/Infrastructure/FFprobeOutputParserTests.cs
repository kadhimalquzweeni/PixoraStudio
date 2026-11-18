using PixoraStudio.Infrastructure.Services;

namespace PixoraStudio.Tests.Infrastructure;

public sealed class FFprobeOutputParserTests
{
    [Fact]
    public void Parse_ShouldReadTypicalFfprobeOutput()
    {
        const string json = """
        {
          "streams": [{"codec_type":"video","width":1920,"height":1080,
            "r_frame_rate":"30000/1001","avg_frame_rate":"30000/1001"}],
          "format": {"duration":"16.416000"}
        }
        """;
        var result = FFprobeOutputParser.Parse(json, "sample.mp4");
        Assert.Equal("sample.mp4", result.FileName);
        Assert.Equal(1920, result.Width);
        Assert.Equal(1080, result.Height);
        Assert.Equal(TimeSpan.FromSeconds(16.416), result.Duration);
        Assert.Equal(30000d / 1001d, result.FramesPerSecond, 8);
    }

    [Fact]
    public void Parse_ShouldUseStreamDurationWhenFormatDurationIsMissing()
    {
        const string json = """
        {"streams":[{"codec_type":"video","width":1280,"height":720,
          "duration":"12.5","avg_frame_rate":"25/1"}],"format":{}}
        """;
        var result = FFprobeOutputParser.Parse(json, "sample.mkv");
        Assert.Equal(TimeSpan.FromSeconds(12.5), result.Duration);
        Assert.Equal(25, result.FramesPerSecond);
    }

    [Fact]
    public void Parse_ShouldCalculateDurationFromDurationTicksAndTimeBase()
    {
        const string json = """
        {"streams":[{"codec_type":"video","width":640,"height":480,
          "duration_ts":90000,"time_base":"1/90000","r_frame_rate":"25/1"}],
          "format":{"duration":"N/A"}}
        """;
        var result = FFprobeOutputParser.Parse(json, "sample.mov");
        Assert.Equal(TimeSpan.FromSeconds(1), result.Duration);
    }

    [Fact]
    public void Parse_ShouldFallBackToNominalFrameRateWhenAverageRateIsInvalid()
    {
        const string json = """
        {"streams":[{"codec_type":"video","width":640,"height":360,
          "r_frame_rate":"30/1","avg_frame_rate":"0/0"}],
          "format":{"duration":4.0}}
        """;
        var result = FFprobeOutputParser.Parse(json, "sample.mp4");
        Assert.Equal(30, result.FramesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(4), result.Duration);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"streams":[]}""")]
    [InlineData("""{"streams":[{"codec_type":"audio"}]}""")]
    public void Parse_ShouldRejectOutputWithoutVideoStream(string json)
    {
        Assert.Throws<InvalidDataException>(() =>
            FFprobeOutputParser.Parse(json, "sample.mp4"));
    }

    [Fact]
    public void Parse_ShouldRejectVideoWithoutUsableDuration()
    {
        const string json = """
        {"streams":[{"codec_type":"video","width":640,"height":480}],"format":{}}
        """;
        var exception = Assert.Throws<InvalidDataException>(() =>
            FFprobeOutputParser.Parse(json, "sample.mp4"));
        Assert.Contains("duration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ShouldRejectInvalidJson()
    {
        Assert.Throws<System.Text.Json.JsonException>(() =>
            FFprobeOutputParser.Parse("{ invalid", "sample.mp4"));
    }
}
