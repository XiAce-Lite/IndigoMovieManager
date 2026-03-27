using IndigoMovieManager.Thumbnail.Engines.IndexRepair;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class VideoIndexRepairServiceTests
{
    [Test]
    public void ShouldRetryRemuxAsVideoOnly_wmvのmuxer失敗なら再試行対象()
    {
        bool result = VideoIndexRepairService.ShouldRetryRemuxAsVideoOnly(
            @"E:\_サムネイル作成困難動画\古い.wmv",
            "av_interleaved_write_frame failed: ffffffea"
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldRetryRemuxAsVideoOnly_asfのtrailer失敗なら再試行対象()
    {
        bool result = VideoIndexRepairService.ShouldRetryRemuxAsVideoOnly(
            @"E:\_サムネイル作成困難動画\古い.asf",
            "av_write_trailer failed: ffffffea"
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldRetryRemuxAsVideoOnly_aviのmuxer失敗なら再試行対象()
    {
        bool result = VideoIndexRepairService.ShouldRetryRemuxAsVideoOnly(
            @"E:\_サムネイル作成困難動画\作成OK\out1.avi",
            "av_interleaved_write_frame failed: ffffffea"
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldRetryRemuxAsVideoOnly_mp4は再試行対象にしない()
    {
        bool result = VideoIndexRepairService.ShouldRetryRemuxAsVideoOnly(
            @"E:\_サムネイル作成困難動画\通常.mp4",
            "av_interleaved_write_frame failed: ffffffea"
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldRetryRemuxAsVideoOnly_wmvでも別失敗なら再試行対象にしない()
    {
        bool result = VideoIndexRepairService.ShouldRetryRemuxAsVideoOnly(
            @"E:\_サムネイル作成困難動画\古い.wmv",
            "find stream info failed: ffffffff"
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldSkipPacketForNonMonotonicDts_前回以下ならskipする()
    {
        bool result = VideoIndexRepairService.ShouldSkipPacketForNonMonotonicDts(
            previousDts: 100,
            currentDts: 100
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldSkipPacketForNonMonotonicDts_初回はskipしない()
    {
        bool result = VideoIndexRepairService.ShouldSkipPacketForNonMonotonicDts(
            previousDts: long.MinValue,
            currentDts: 100
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryNormalizeMissingTimestamp_ptsだけ欠けていればdtsで補完する()
    {
        long pts = FFmpeg.AutoGen.ffmpeg.AV_NOPTS_VALUE;
        long dts = 120;

        bool normalized = VideoIndexRepairService.TryNormalizeMissingTimestamp(ref pts, ref dts);

        Assert.That(normalized, Is.True);
        Assert.That(pts, Is.EqualTo(120));
        Assert.That(dts, Is.EqualTo(120));
    }

    [Test]
    public void TryNormalizeMissingTimestamp_dtsだけ欠けていればptsで補完する()
    {
        long pts = 240;
        long dts = FFmpeg.AutoGen.ffmpeg.AV_NOPTS_VALUE;

        bool normalized = VideoIndexRepairService.TryNormalizeMissingTimestamp(ref pts, ref dts);

        Assert.That(normalized, Is.True);
        Assert.That(pts, Is.EqualTo(240));
        Assert.That(dts, Is.EqualTo(240));
    }

    [Test]
    public void ShouldSkipPacketForUnknownTimestamp_ptsとdtsが両方欠けていればskipする()
    {
        bool result = VideoIndexRepairService.ShouldSkipPacketForUnknownTimestamp(
            FFmpeg.AutoGen.ffmpeg.AV_NOPTS_VALUE,
            FFmpeg.AutoGen.ffmpeg.AV_NOPTS_VALUE
        );

        Assert.That(result, Is.True);
    }
}
