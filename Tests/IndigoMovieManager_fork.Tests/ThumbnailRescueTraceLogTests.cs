using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailRescueTraceLogTests
{
    [TestCase("1", true)]
    [TestCase("true", true)]
    [TestCase("on", true)]
    [TestCase("yes", true)]
    [TestCase("0", false)]
    [TestCase("", false)]
    public void IsEnabledValue_環境変数の真偽を解釈する(string rawValue, bool expected)
    {
        bool actual = ThumbnailRescueTraceLog.IsEnabledValue(rawValue);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void IsEnabledValue_nullはfalseとして扱う()
    {
        bool actual = ThumbnailRescueTraceLog.IsEnabledValue(null);

        Assert.That(actual, Is.False);
    }

    [Test]
    public void BuildPanelSizeLabel_Tab2は160x120x1x1を返す()
    {
        string label = ThumbnailRescueTraceLog.BuildPanelSizeLabel(2, "anime");

        Assert.That(label, Is.EqualTo("160x120x1x1"));
    }

    [Test]
    public void BuildCsvLine_クォートと改行を安全にする()
    {
        string line = ThumbnailRescueTraceLog.BuildCsvLine(
            new DateTime(2026, 3, 15, 8, 30, 0, DateTimeKind.Utc),
            source: "worker",
            failureId: 12,
            moviePath: @"C:\movie space\sample.mp4",
            tabIndex: 2,
            panelSize: "160x120x1x1",
            routeId: "route-long-no-frames",
            symptomClass: "long-no-frames",
            phase: "direct_engine_attempt",
            engine: "ffmpeg1pass",
            action: "engine_attempt",
            result: "failed",
            elapsedMs: 1234,
            failureKind: "TransientDecodeFailure",
            reason: "line1\r\nline2, \"quoted\"",
            outputPath: @"C:\thumb\out 1.jpg"
        );

        Assert.That(line, Does.Contain("\"C:\\movie space\\sample.mp4\""));
        Assert.That(line, Does.Contain("\"line1  line2, \"\"quoted\"\"\""));
        Assert.That(line, Does.Contain("\"C:\\thumb\\out 1.jpg\""));
        Assert.That(line.Contains('\r') || line.Contains('\n'), Is.False);
    }
}
