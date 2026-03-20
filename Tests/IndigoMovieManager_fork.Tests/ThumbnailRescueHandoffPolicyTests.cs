using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailRescueHandoffPolicyTests
{
    [Test]
    public void ResolveHandoffType_timeout例外ならTimeoutを返す()
    {
        string handoffType = ThumbnailRescueHandoffPolicy.ResolveHandoffType(
            new TimeoutException("thumbnail normal lane timeout")
        );

        Assert.That(handoffType, Is.EqualTo("timeout"));
    }

    [Test]
    public void ResolveHandoffType_timeout文言ならTimeoutを返す()
    {
        string handoffType = ThumbnailRescueHandoffPolicy.ResolveHandoffType(
            ex: null,
            failureReasonOverride:
                "engine attempt timeout: failure_id=25 engine=opencv timeout_sec=300"
        );

        Assert.That(handoffType, Is.EqualTo("timeout"));
    }

    [Test]
    public void ResolveHandoffType_通常失敗ならFailureを返す()
    {
        string handoffType = ThumbnailRescueHandoffPolicy.ResolveHandoffType(
            new InvalidOperationException("thumbnail create failed")
        );

        Assert.That(handoffType, Is.EqualTo("failure"));
    }

    [Test]
    public void ResolveFailureKind_frameDecodeFailedならIndexCorruptionへ寄せる()
    {
        ThumbnailFailureKind kind = ThumbnailRescueHandoffPolicy.ResolveFailureKind(
            ex: null,
            moviePath: "",
            failureReasonOverride: "frame decode failed at sec=14871"
        );

        Assert.That(kind, Is.EqualTo(ThumbnailFailureKind.IndexCorruption));
    }

    [Test]
    public void ResolveFailureKind_OnePassFailedならTransientDecodeFailureへ寄せる()
    {
        ThumbnailFailureKind kind = ThumbnailRescueHandoffPolicy.ResolveFailureKind(
            ex: null,
            moviePath: "",
            failureReasonOverride: "ffmpeg one-pass failed"
        );

        Assert.That(kind, Is.EqualTo(ThumbnailFailureKind.TransientDecodeFailure));
    }

    [Test]
    public void ResolveFailureKind_VideoStreamNotFoundならNoVideo扱いにする()
    {
        ThumbnailFailureKind kind = ThumbnailRescueHandoffPolicy.ResolveFailureKind(
            ex: null,
            moviePath: "",
            failureReasonOverride: "video stream not found"
        );

        Assert.That(kind, Is.EqualTo(ThumbnailFailureKind.NoVideoStream));
    }

    [Test]
    public void ResolveFailureKind_timeout文言だけでもHang扱いにする()
    {
        ThumbnailFailureKind kind = ThumbnailRescueHandoffPolicy.ResolveFailureKind(
            ex: null,
            moviePath: "",
            failureReasonOverride: "thumbnail normal lane timeout: timeout_sec=10"
        );

        Assert.That(kind, Is.EqualTo(ThumbnailFailureKind.HangSuspected));
    }

    [Test]
    public void ResolveLaneName_遅延フラグに従ってNormalとSlowを返す()
    {
        string normalLane = ThumbnailRescueHandoffPolicy.ResolveLaneName(isSlowLane: false);
        string slowLane = ThumbnailRescueHandoffPolicy.ResolveLaneName(isSlowLane: true);

        Assert.That(normalLane, Is.EqualTo("normal"));
        Assert.That(slowLane, Is.EqualTo("slow"));
    }
}
