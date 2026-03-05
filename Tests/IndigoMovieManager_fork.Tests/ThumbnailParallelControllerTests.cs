using System.Reflection;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailParallelControllerTests
{
    private int originalFastScaleUpStep;
    private int originalFastStableWindows;
    private int originalFastScaleUpCooldownSec;
    private int originalScaleUpBlockedAfterDownSec;
    private int originalFastRecoveryWindowSec;

    [SetUp]
    public void SetUp()
    {
        var settings = IndigoMovieManager.Properties.Settings.Default;
        originalFastScaleUpStep = settings.ThumbnailParallelFastRecoveryScaleUpStep;
        originalFastStableWindows = settings.ThumbnailParallelFastRecoveryStableWindows;
        originalFastScaleUpCooldownSec = settings.ThumbnailParallelFastRecoveryScaleUpCooldownSec;
        originalScaleUpBlockedAfterDownSec = settings.ThumbnailParallelScaleUpBlockedAfterDownSec;
        originalFastRecoveryWindowSec = settings.ThumbnailParallelFastRecoveryWindowSec;

        // テストは既定値固定で実行し、ローカル user.config の影響を受けないようにする。
        settings.ThumbnailParallelFastRecoveryScaleUpStep = 2;
        settings.ThumbnailParallelFastRecoveryStableWindows = 1;
        settings.ThumbnailParallelFastRecoveryScaleUpCooldownSec = 12;
        settings.ThumbnailParallelScaleUpBlockedAfterDownSec = 15;
        settings.ThumbnailParallelFastRecoveryWindowSec = 180;
    }

    [TearDown]
    public void TearDown()
    {
        var settings = IndigoMovieManager.Properties.Settings.Default;
        settings.ThumbnailParallelFastRecoveryScaleUpStep = originalFastScaleUpStep;
        settings.ThumbnailParallelFastRecoveryStableWindows = originalFastStableWindows;
        settings.ThumbnailParallelFastRecoveryScaleUpCooldownSec = originalFastScaleUpCooldownSec;
        settings.ThumbnailParallelScaleUpBlockedAfterDownSec = originalScaleUpBlockedAfterDownSec;
        settings.ThumbnailParallelFastRecoveryWindowSec = originalFastRecoveryWindowSec;
    }

    [Test]
    public void EvaluateNext_減速直後は1ウィンドウで素早く復帰する()
    {
        ThumbnailParallelController controller = new(initialParallelism: 8);
        ThumbnailEngineRuntimeSnapshot emptySnapshot = new(0, 0, 0);

        int scaledDown = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 3,
            queueActiveCount: 100,
            engineSnapshot: emptySnapshot,
            log: null
        );
        Assert.That(scaledDown, Is.EqualTo(6));

        // 減速直後の復帰条件だけ満たすため、最小ブロック時間を越えた時刻に寄せる。
        SetPrivateDateTimeField(controller, "lastScaleDownUtc", DateTime.UtcNow.AddSeconds(-20));

        int recovered = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 100,
            engineSnapshot: emptySnapshot,
            log: null
        );

        Assert.That(recovered, Is.EqualTo(8));
    }

    [Test]
    public void EvaluateNext_通常時は2ウィンドウで段階的に上昇する()
    {
        ThumbnailParallelController controller = new(initialParallelism: 6);
        ThumbnailEngineRuntimeSnapshot emptySnapshot = new(0, 0, 0);

        // 通常モードを再現するため、直近減速を十分過去に置く。
        SetPrivateDateTimeField(controller, "lastScaleDownUtc", DateTime.UtcNow.AddMinutes(-10));

        int first = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 100,
            engineSnapshot: emptySnapshot,
            log: null
        );
        int second = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 100,
            engineSnapshot: emptySnapshot,
            log: null
        );

        Assert.That(first, Is.EqualTo(6));
        Assert.That(second, Is.EqualTo(7));
    }

    [Test]
    public void EnsureWithinConfigured_設定値増加時は即時に追従する()
    {
        ThumbnailParallelController controller = new(initialParallelism: 2);

        int first = controller.EnsureWithinConfigured(configuredParallelism: 2);
        int raised = controller.EnsureWithinConfigured(configuredParallelism: 8);

        Assert.That(first, Is.EqualTo(2));
        Assert.That(raised, Is.EqualTo(8));
    }

    private static void SetPrivateDateTimeField(
        ThumbnailParallelController controller,
        string fieldName,
        DateTime value
    )
    {
        FieldInfo? field = typeof(ThumbnailParallelController).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        field!.SetValue(controller, value);
    }
}
