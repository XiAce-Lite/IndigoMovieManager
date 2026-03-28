using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailProgressEcoModeTests
{
    [Test]
    public void ResolveThumbnailFfmpegOnePassEcoHint_低速プリセットは1thread_idleになる()
    {
        (int? threadCount, string priority) = MainWindow.ResolveThumbnailFfmpegOnePassEcoHint(
            configuredParallelism: 2,
            slowLaneMinGb: 50
        );

        Assert.That(threadCount, Is.EqualTo(1));
        Assert.That(priority, Is.EqualTo("idle"));
    }

    [Test]
    public void ResolveThumbnailFfmpegOnePassEcoHint_普通プリセットは2thread_below_normalになる()
    {
        int normalParallelism = System.Math.Max(1, System.Environment.ProcessorCount / 3);
        (int? threadCount, string priority) = MainWindow.ResolveThumbnailFfmpegOnePassEcoHint(
            configuredParallelism: normalParallelism,
            slowLaneMinGb: 100
        );

        Assert.That(threadCount, Is.EqualTo(2));
        Assert.That(priority, Is.EqualTo("below_normal"));
    }

    [Test]
    public void ResolveThumbnailFfmpegOnePassEcoHint_高速プリセットは既定へ戻す()
    {
        int fastParallelism = System.Math.Max(1, System.Environment.ProcessorCount / 2);
        (int? threadCount, string priority) = MainWindow.ResolveThumbnailFfmpegOnePassEcoHint(
            configuredParallelism: fastParallelism,
            slowLaneMinGb: 100
        );

        Assert.That(threadCount, Is.Null);
        Assert.That(priority, Is.Empty);
    }

    [Test]
    public void ResolveThumbnailFfmpegOnePassEcoHint_Customでも並列2以下なら強エコに寄せる()
    {
        (int? threadCount, string priority) = MainWindow.ResolveThumbnailFfmpegOnePassEcoHint(
            configuredParallelism: 2,
            slowLaneMinGb: 200
        );

        Assert.That(threadCount, Is.EqualTo(1));
        Assert.That(priority, Is.EqualTo("idle"));
    }
}
