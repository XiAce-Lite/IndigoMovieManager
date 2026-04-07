using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ManualThumbnailRescuePopupTests
{
    [Test]
    public void TryExtractManualThumbnailRescueSuccessInfo_successログからfailureIdとoutputを抜ける()
    {
        bool parsed = MainWindow.TryExtractManualThumbnailRescueSuccessInfo(
            "manual-slot: rescue worker stdout: rescue succeeded: failure_id=123 phase=direct engine=ffmpeg1pass output='C:\\thumbs\\ok.#hash.jpg'",
            out long failureId,
            out string outputThumbPath
        );

        Assert.That(parsed, Is.True);
        Assert.That(failureId, Is.EqualTo(123));
        Assert.That(outputThumbPath, Is.EqualTo(@"C:\thumbs\ok.#hash.jpg"));
    }

    [Test]
    public void TryExtractManualThumbnailRescueSuccessInfo_success以外や欠損はFalseを返す()
    {
        bool notSuccess = MainWindow.TryExtractManualThumbnailRescueSuccessInfo(
            "manual-slot: rescue worker stdout: rescue gave up: failure_id=123 phase=direct reason='failed'",
            out long failureId,
            out string outputThumbPath
        );
        bool missingOutput = MainWindow.TryExtractManualThumbnailRescueSuccessInfo(
            "manual-slot: rescue worker stdout: rescue succeeded: failure_id=123 phase=direct engine=ffmpeg1pass",
            out long missingOutputFailureId,
            out string missingOutputPath
        );

        Assert.That(notSuccess, Is.False);
        Assert.That(failureId, Is.EqualTo(0));
        Assert.That(outputThumbPath, Is.Empty);
        Assert.That(missingOutput, Is.False);
        Assert.That(missingOutputFailureId, Is.EqualTo(0));
        Assert.That(missingOutputPath, Is.Empty);
    }

    [Test]
    public void TryBuildManualThumbnailRescueProgressDetailMessage_engine試行開始を日本語化できる()
    {
        bool parsed = MainWindow.TryBuildManualThumbnailRescueProgressDetailMessage(
            "manual-slot: rescue worker stdout: engine attempt start: failure_id=123 engine=ffmpeg1pass timeout_sec=120 repair=False source='C:\\movie.mkv'",
            out string progressMessage
        );

        Assert.That(parsed, Is.True);
        Assert.That(progressMessage, Is.EqualTo("ffmpeg1pass 試行中"));
    }

    [Test]
    public void TryBuildManualThumbnailRescueProgressDetailMessage_修復開始を日本語化できる()
    {
        bool parsed = MainWindow.TryBuildManualThumbnailRescueProgressDetailMessage(
            "manual-slot: rescue worker stdout: repair start: failure_id=123 timeout_sec=300 output='C:\\repair\\movie.mkv'",
            out string progressMessage
        );

        Assert.That(parsed, Is.True);
        Assert.That(progressMessage, Is.EqualTo("インデックス修復中"));
    }

    [Test]
    public void TryBuildManualThumbnailRescueProgressDetailMessage_黒フレーム再試行を日本語化できる()
    {
        bool parsed = MainWindow.TryBuildManualThumbnailRescueProgressDetailMessage(
            "manual-slot: rescue worker stdout: black retry start: failure_id=123 engine=ffmpeg1pass retry=2/5 secs=12.5 repair=False",
            out string progressMessage
        );

        Assert.That(parsed, Is.True);
        Assert.That(progressMessage, Is.EqualTo("黒フレーム再試行 2/5"));
    }
}
