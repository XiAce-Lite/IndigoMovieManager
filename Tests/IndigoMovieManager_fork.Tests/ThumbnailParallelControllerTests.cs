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
    private double originalHighLoadWeightError;
    private double originalHighLoadWeightQueuePressure;
    private double originalHighLoadWeightSlowBacklog;
    private double originalHighLoadWeightRecoveryBacklog;
    private double originalHighLoadWeightThroughputPenalty;
    private double originalHighLoadWeightThermalWarning;
    private double originalHighLoadWeightUsnMftBusy;
    private double originalHighLoadRecoveryThreshold;
    private double originalHighLoadMildThreshold;
    private double originalHighLoadThreshold;
    private double originalHighLoadDangerThreshold;

    [SetUp]
    public void SetUp()
    {
        var settings = IndigoMovieManager.Properties.Settings.Default;
        originalFastScaleUpStep = settings.ThumbnailParallelFastRecoveryScaleUpStep;
        originalFastStableWindows = settings.ThumbnailParallelFastRecoveryStableWindows;
        originalFastScaleUpCooldownSec = settings.ThumbnailParallelFastRecoveryScaleUpCooldownSec;
        originalScaleUpBlockedAfterDownSec = settings.ThumbnailParallelScaleUpBlockedAfterDownSec;
        originalFastRecoveryWindowSec = settings.ThumbnailParallelFastRecoveryWindowSec;
        originalHighLoadWeightError = settings.ThumbnailParallelHighLoadWeightError;
        originalHighLoadWeightQueuePressure = settings.ThumbnailParallelHighLoadWeightQueuePressure;
        originalHighLoadWeightSlowBacklog = settings.ThumbnailParallelHighLoadWeightSlowBacklog;
        originalHighLoadWeightRecoveryBacklog =
            settings.ThumbnailParallelHighLoadWeightRecoveryBacklog;
        originalHighLoadWeightThroughputPenalty =
            settings.ThumbnailParallelHighLoadWeightThroughputPenalty;
        originalHighLoadWeightThermalWarning =
            settings.ThumbnailParallelHighLoadWeightThermalWarning;
        originalHighLoadWeightUsnMftBusy = settings.ThumbnailParallelHighLoadWeightUsnMftBusy;
        originalHighLoadRecoveryThreshold = settings.ThumbnailParallelHighLoadRecoveryThreshold;
        originalHighLoadMildThreshold = settings.ThumbnailParallelHighLoadMildThreshold;
        originalHighLoadThreshold = settings.ThumbnailParallelHighLoadThreshold;
        originalHighLoadDangerThreshold = settings.ThumbnailParallelHighLoadDangerThreshold;

        // テストは既定値固定で実行し、ローカル user.config の影響を受けないようにする。
        settings.ThumbnailParallelFastRecoveryScaleUpStep = 2;
        settings.ThumbnailParallelFastRecoveryStableWindows = 1;
        settings.ThumbnailParallelFastRecoveryScaleUpCooldownSec = 12;
        settings.ThumbnailParallelScaleUpBlockedAfterDownSec = 15;
        settings.ThumbnailParallelFastRecoveryWindowSec = 180;
        settings.ThumbnailParallelHighLoadWeightError = 0.30d;
        settings.ThumbnailParallelHighLoadWeightQueuePressure = 0.25d;
        settings.ThumbnailParallelHighLoadWeightSlowBacklog = 0.10d;
        settings.ThumbnailParallelHighLoadWeightRecoveryBacklog = 0.10d;
        settings.ThumbnailParallelHighLoadWeightThroughputPenalty = 0.10d;
        settings.ThumbnailParallelHighLoadWeightThermalWarning = 0.20d;
        settings.ThumbnailParallelHighLoadWeightUsnMftBusy = 0.10d;
        settings.ThumbnailParallelHighLoadRecoveryThreshold = 0.48d;
        settings.ThumbnailParallelHighLoadMildThreshold = 0.60d;
        settings.ThumbnailParallelHighLoadThreshold = 0.82d;
        settings.ThumbnailParallelHighLoadDangerThreshold = 0.95d;
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
        settings.ThumbnailParallelHighLoadWeightError = originalHighLoadWeightError;
        settings.ThumbnailParallelHighLoadWeightQueuePressure =
            originalHighLoadWeightQueuePressure;
        settings.ThumbnailParallelHighLoadWeightSlowBacklog = originalHighLoadWeightSlowBacklog;
        settings.ThumbnailParallelHighLoadWeightRecoveryBacklog =
            originalHighLoadWeightRecoveryBacklog;
        settings.ThumbnailParallelHighLoadWeightThroughputPenalty =
            originalHighLoadWeightThroughputPenalty;
        settings.ThumbnailParallelHighLoadWeightThermalWarning =
            originalHighLoadWeightThermalWarning;
        settings.ThumbnailParallelHighLoadWeightUsnMftBusy = originalHighLoadWeightUsnMftBusy;
        settings.ThumbnailParallelHighLoadRecoveryThreshold =
            originalHighLoadRecoveryThreshold;
        settings.ThumbnailParallelHighLoadMildThreshold = originalHighLoadMildThreshold;
        settings.ThumbnailParallelHighLoadThreshold = originalHighLoadThreshold;
        settings.ThumbnailParallelHighLoadDangerThreshold = originalHighLoadDangerThreshold;
    }

    [Test]
    public void EvaluateNext_減速直後は1ウィンドウで素早く復帰する()
    {
        ThumbnailParallelController controller = new(initialParallelism: 8);
        ThumbnailEngineRuntimeSnapshot emptySnapshot = new(0, 0, 0);
        List<string> logs = [];

        int scaledDown = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 3,
            queueActiveCount: 100,
            engineSnapshot: emptySnapshot,
            log: logs.Add
        );
        Assert.That(scaledDown, Is.EqualTo(6));
        Assert.That(logs.Any(x => x.Contains("category=error")), Is.True);

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

    [Test]
    public void EvaluateNext_動的復帰禁止時は安定しても上げない()
    {
        ThumbnailParallelController controller = new(initialParallelism: 4);
        ThumbnailEngineRuntimeSnapshot emptySnapshot = new(0, 0, 0);

        SetPrivateDateTimeField(controller, "lastScaleDownUtc", DateTime.UtcNow.AddMinutes(-10));

        int first = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 100,
            engineSnapshot: emptySnapshot,
            log: null,
            dynamicMinimumParallelism: 2,
            allowScaleUp: false,
            scaleUpDemandFactor: 2
        );
        int second = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 100,
            engineSnapshot: emptySnapshot,
            log: null,
            dynamicMinimumParallelism: 2,
            allowScaleUp: false,
            scaleUpDemandFactor: 2
        );

        Assert.That(first, Is.EqualTo(4));
        Assert.That(second, Is.EqualTo(4));
    }

    [Test]
    public void EvaluateNext_需要係数が高い時は同じ滞留でも復帰しない()
    {
        ThumbnailParallelController controller = new(initialParallelism: 4);
        ThumbnailEngineRuntimeSnapshot emptySnapshot = new(0, 0, 0);

        SetPrivateDateTimeField(controller, "lastScaleDownUtc", DateTime.UtcNow.AddMinutes(-10));

        int first = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 10,
            engineSnapshot: emptySnapshot,
            log: null,
            dynamicMinimumParallelism: 4,
            allowScaleUp: true,
            scaleUpDemandFactor: 4
        );
        int second = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 10,
            engineSnapshot: emptySnapshot,
            log: null,
            dynamicMinimumParallelism: 4,
            allowScaleUp: true,
            scaleUpDemandFactor: 4
        );

        Assert.That(first, Is.EqualTo(4));
        Assert.That(second, Is.EqualTo(4));
    }

    [Test]
    public void CalculateHighLoadScore_平常窓では軽度閾値を超えない()
    {
        ThumbnailHighLoadScoreResult score = ThumbnailParallelController.CalculateHighLoadScore(
            new ThumbnailHighLoadInput(
                batchProcessedCount: 10,
                batchFailedCount: 0,
                batchElapsedMs: 5000,
                queueActiveCount: 4,
                currentParallelism: 4,
                configuredParallelism: 4,
                hasSlowDemand: false,
                hasRecoveryDemand: false,
                engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0)
            )
        );

        Assert.That(score.HighLoadScore, Is.LessThan(0.55d));
        Assert.That(score.IsMildHighLoad, Is.False);
        Assert.That(score.IsDanger, Is.False);
    }

    [Test]
    public void CalculateHighLoadScore_滞留と失敗が重なると危険域に入る()
    {
        ThumbnailHighLoadScoreResult score = ThumbnailParallelController.CalculateHighLoadScore(
            new ThumbnailHighLoadInput(
                batchProcessedCount: 2,
                batchFailedCount: 2,
                batchElapsedMs: 18000,
                queueActiveCount: 40,
                currentParallelism: 6,
                configuredParallelism: 10,
                hasSlowDemand: true,
                hasRecoveryDemand: true,
                engineSnapshot: new ThumbnailEngineRuntimeSnapshot(2, 0, 2),
                thermalState: ThumbnailThermalSignalLevel.Warning,
                usnMftState: ThumbnailUsnMftSignalLevel.Busy,
                usnMftLastScanLatencyMs: 6400,
                usnMftJournalBacklogCount: 18
            )
        );

        Assert.That(score.HighLoadScore, Is.GreaterThanOrEqualTo(0.95d));
        Assert.That(score.IsDanger, Is.True);
    }

    [Test]
    public void CalculateHighLoadScore_処理0件でも滞留があればスループット悪化を最大評価する()
    {
        ThumbnailHighLoadScoreResult score = ThumbnailParallelController.CalculateHighLoadScore(
            new ThumbnailHighLoadInput(
                batchProcessedCount: 0,
                batchFailedCount: 0,
                batchElapsedMs: 0,
                queueActiveCount: 12,
                currentParallelism: 4,
                configuredParallelism: 8,
                hasSlowDemand: false,
                hasRecoveryDemand: false,
                engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0)
            )
        );

        Assert.That(score.ThroughputPenaltyScore, Is.EqualTo(1.0d));
        Assert.That(score.QueuePressureScore, Is.GreaterThan(0.0d));
        Assert.That(score.HighLoadScore, Is.GreaterThan(0.0d));
    }

    [Test]
    public void CalculateHighLoadScore_再試行成功が多い窓はエラースコアを少し下げる()
    {
        ThumbnailHighLoadScoreResult withoutRetrySuccess =
            ThumbnailParallelController.CalculateHighLoadScore(
                new ThumbnailHighLoadInput(
                    batchProcessedCount: 10,
                    batchFailedCount: 2,
                    batchElapsedMs: 8000,
                    queueActiveCount: 10,
                    currentParallelism: 4,
                    configuredParallelism: 8,
                    hasSlowDemand: false,
                    hasRecoveryDemand: false,
                    engineSnapshot: new ThumbnailEngineRuntimeSnapshot(2, 0, 1)
                )
            );
        ThumbnailHighLoadScoreResult withRetrySuccess =
            ThumbnailParallelController.CalculateHighLoadScore(
                new ThumbnailHighLoadInput(
                    batchProcessedCount: 10,
                    batchFailedCount: 2,
                    batchElapsedMs: 8000,
                    queueActiveCount: 10,
                    currentParallelism: 4,
                    configuredParallelism: 8,
                    hasSlowDemand: false,
                    hasRecoveryDemand: false,
                    engineSnapshot: new ThumbnailEngineRuntimeSnapshot(2, 4, 1)
                )
            );

        Assert.That(withRetrySuccess.ErrorScore, Is.LessThan(withoutRetrySuccess.ErrorScore));
        Assert.That(withRetrySuccess.HighLoadScore, Is.LessThan(withoutRetrySuccess.HighLoadScore));
    }

    [Test]
    public void CalculateHighLoadScore_温度警告があるとスコアを加点する()
    {
        ThumbnailHighLoadScoreResult normal = ThumbnailParallelController.CalculateHighLoadScore(
            new ThumbnailHighLoadInput(
                batchProcessedCount: 10,
                batchFailedCount: 0,
                batchElapsedMs: 5000,
                queueActiveCount: 4,
                currentParallelism: 4,
                configuredParallelism: 4,
                hasSlowDemand: false,
                hasRecoveryDemand: false,
                engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0),
                thermalState: ThumbnailThermalSignalLevel.Normal
            )
        );
        ThumbnailHighLoadScoreResult warning = ThumbnailParallelController.CalculateHighLoadScore(
            new ThumbnailHighLoadInput(
                batchProcessedCount: 10,
                batchFailedCount: 0,
                batchElapsedMs: 5000,
                queueActiveCount: 4,
                currentParallelism: 4,
                configuredParallelism: 4,
                hasSlowDemand: false,
                hasRecoveryDemand: false,
                engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0),
                thermalState: ThumbnailThermalSignalLevel.Warning
            )
        );

        Assert.That(warning.ThermalScore, Is.GreaterThan(0.0d));
        Assert.That(warning.HighLoadScore, Is.GreaterThan(normal.HighLoadScore));
    }

    [Test]
    public void CalculateHighLoadScore_UsnMftBusyがあるとスコアを加点する()
    {
        ThumbnailHighLoadScoreResult normal = ThumbnailParallelController.CalculateHighLoadScore(
            new ThumbnailHighLoadInput(
                batchProcessedCount: 10,
                batchFailedCount: 0,
                batchElapsedMs: 5000,
                queueActiveCount: 4,
                currentParallelism: 4,
                configuredParallelism: 4,
                hasSlowDemand: false,
                hasRecoveryDemand: false,
                engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0),
                usnMftState: ThumbnailUsnMftSignalLevel.Ready,
                usnMftLastScanLatencyMs: 1200,
                usnMftJournalBacklogCount: 0
            )
        );
        ThumbnailHighLoadScoreResult busy = ThumbnailParallelController.CalculateHighLoadScore(
            new ThumbnailHighLoadInput(
                batchProcessedCount: 10,
                batchFailedCount: 0,
                batchElapsedMs: 5000,
                queueActiveCount: 4,
                currentParallelism: 4,
                configuredParallelism: 4,
                hasSlowDemand: false,
                hasRecoveryDemand: false,
                engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0),
                usnMftState: ThumbnailUsnMftSignalLevel.Busy,
                usnMftLastScanLatencyMs: 6400,
                usnMftJournalBacklogCount: 18
            )
        );

        Assert.That(busy.UsnMftScore, Is.GreaterThan(0.0d));
        Assert.That(busy.HighLoadScore, Is.GreaterThan(normal.HighLoadScore));
    }

    [Test]
    public void CalculateHighLoadScore_UsnMftAccessDeniedは高負荷へ混ぜない()
    {
        ThumbnailHighLoadScoreResult accessDenied =
            ThumbnailParallelController.CalculateHighLoadScore(
                new ThumbnailHighLoadInput(
                    batchProcessedCount: 10,
                    batchFailedCount: 0,
                    batchElapsedMs: 5000,
                    queueActiveCount: 4,
                    currentParallelism: 4,
                    configuredParallelism: 4,
                    hasSlowDemand: false,
                    hasRecoveryDemand: false,
                    engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0),
                    usnMftState: ThumbnailUsnMftSignalLevel.AccessDenied,
                    usnMftLastScanLatencyMs: 6400,
                    usnMftJournalBacklogCount: 18
                )
            );

        Assert.That(accessDenied.UsnMftScore, Is.EqualTo(0.0d));
    }

    [Test]
    public void CalculateHighLoadScore_軽度閾値設定を上げると判定へ反映する()
    {
        var settings = IndigoMovieManager.Properties.Settings.Default;
        ThumbnailHighLoadInput input = new(
            batchProcessedCount: 10,
            batchFailedCount: 0,
            batchElapsedMs: 22000,
            queueActiveCount: 20,
            currentParallelism: 6,
            configuredParallelism: 8,
            hasSlowDemand: true,
            hasRecoveryDemand: true,
            engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0),
            thermalState: ThumbnailThermalSignalLevel.Warning,
            usnMftState: ThumbnailUsnMftSignalLevel.Busy,
            usnMftLastScanLatencyMs: 6400,
            usnMftJournalBacklogCount: 18
        );

        ThumbnailHighLoadScoreResult defaultThresholdScore =
            ThumbnailParallelController.CalculateHighLoadScore(input);

        settings.ThumbnailParallelHighLoadMildThreshold = 0.70d;
        ThumbnailHighLoadScoreResult raisedThresholdScore =
            ThumbnailParallelController.CalculateHighLoadScore(input);

        Assert.That(defaultThresholdScore.IsMildHighLoad, Is.True);
        Assert.That(raisedThresholdScore.IsMildHighLoad, Is.False);
    }

    [Test]
    public void CalculateHighLoadScore_中立帯では復帰窓にならない()
    {
        ThumbnailHighLoadScoreResult score = ThumbnailParallelController.CalculateHighLoadScore(
            new ThumbnailHighLoadInput(
                batchProcessedCount: 10,
                batchFailedCount: 1,
                batchElapsedMs: 55000,
                queueActiveCount: 10,
                currentParallelism: 4,
                configuredParallelism: 8,
                hasSlowDemand: true,
                hasRecoveryDemand: true,
                engineSnapshot: new ThumbnailEngineRuntimeSnapshot(1, 0, 0)
            )
        );

        Assert.That(score.HighLoadScore, Is.GreaterThan(0.48d).And.LessThan(0.60d));
        Assert.That(score.IsRecoveryWindow, Is.False);
        Assert.That(score.IsMildHighLoad, Is.False);
    }

    [Test]
    public void EvaluateNext_軽度高負荷では1段階だけ縮退する()
    {
        ThumbnailParallelController controller = new(initialParallelism: 6);
        ThumbnailEngineRuntimeSnapshot emptySnapshot = new(0, 0, 0);
        List<string> logs = [];
        ThumbnailHighLoadInput highLoadInput = new(
            batchProcessedCount: 10,
            batchFailedCount: 0,
            batchElapsedMs: 22000,
            queueActiveCount: 20,
            currentParallelism: 6,
            configuredParallelism: 8,
            hasSlowDemand: true,
            hasRecoveryDemand: true,
            engineSnapshot: emptySnapshot,
            thermalState: ThumbnailThermalSignalLevel.Warning,
            usnMftState: ThumbnailUsnMftSignalLevel.Busy,
            usnMftLastScanLatencyMs: 6400,
            usnMftJournalBacklogCount: 18
        );

        int next = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 20,
            engineSnapshot: emptySnapshot,
            log: logs.Add,
            dynamicMinimumParallelism: 2,
            allowScaleUp: true,
            scaleUpDemandFactor: 2,
            highLoadInput: highLoadInput
        );

        Assert.That(next, Is.EqualTo(5));
        Assert.That(logs.Any(x => x.Contains("category=high-load")), Is.True);
    }

    [Test]
    public void EvaluateNext_危険域高負荷では動的下限まで落とす()
    {
        ThumbnailParallelController controller = new(initialParallelism: 8);
        ThumbnailEngineRuntimeSnapshot heavySnapshot = new(2, 0, 2);
        ThumbnailHighLoadInput highLoadInput = new(
            batchProcessedCount: 2,
            batchFailedCount: 2,
            batchElapsedMs: 18000,
            queueActiveCount: 40,
            currentParallelism: 8,
            configuredParallelism: 10,
            hasSlowDemand: true,
            hasRecoveryDemand: true,
            engineSnapshot: heavySnapshot,
            thermalState: ThumbnailThermalSignalLevel.Warning,
            usnMftState: ThumbnailUsnMftSignalLevel.Busy,
            usnMftLastScanLatencyMs: 6400,
            usnMftJournalBacklogCount: 18
        );

        int next = controller.EvaluateNext(
            configuredParallelism: 10,
            batchProcessedCount: 2,
            batchFailedCount: 2,
            queueActiveCount: 40,
            engineSnapshot: heavySnapshot,
            log: null,
            dynamicMinimumParallelism: 4,
            allowScaleUp: true,
            scaleUpDemandFactor: 2,
            highLoadInput: highLoadInput
        );

        Assert.That(next, Is.EqualTo(4));
    }

    [Test]
    public void EvaluateNext_温度危険域では即時に動的下限まで落とす()
    {
        ThumbnailParallelController controller = new(initialParallelism: 8);
        ThumbnailEngineRuntimeSnapshot snapshot = new(0, 0, 0);
        List<string> logs = [];
        ThumbnailHighLoadInput thermalCriticalInput = new(
            batchProcessedCount: 10,
            batchFailedCount: 0,
            batchElapsedMs: 5000,
            queueActiveCount: 2,
            currentParallelism: 8,
            configuredParallelism: 10,
            hasSlowDemand: false,
            hasRecoveryDemand: false,
            engineSnapshot: snapshot,
            thermalState: ThumbnailThermalSignalLevel.Critical
        );

        int next = controller.EvaluateNext(
            configuredParallelism: 10,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 2,
            engineSnapshot: snapshot,
            log: logs.Add,
            dynamicMinimumParallelism: 4,
            allowScaleUp: true,
            scaleUpDemandFactor: 2,
            highLoadInput: thermalCriticalInput
        );

        Assert.That(next, Is.EqualTo(4));
        Assert.That(logs.Any(x => x.Contains("mode=thermal-critical")), Is.True);
    }

    [Test]
    public void EvaluateNext_中立帯高負荷では安定扱いせず復帰しない()
    {
        ThumbnailParallelController controller = new(initialParallelism: 4);
        ThumbnailEngineRuntimeSnapshot snapshot = new(1, 0, 0);
        ThumbnailHighLoadInput neutralHighLoadInput = new(
            batchProcessedCount: 10,
            batchFailedCount: 1,
            batchElapsedMs: 55000,
            queueActiveCount: 100,
            currentParallelism: 4,
            configuredParallelism: 8,
            hasSlowDemand: true,
            hasRecoveryDemand: true,
            engineSnapshot: snapshot
        );

        SetPrivateDateTimeField(controller, "lastScaleDownUtc", DateTime.UtcNow.AddMinutes(-10));

        int first = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 100,
            engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0),
            log: null,
            dynamicMinimumParallelism: 4,
            allowScaleUp: true,
            scaleUpDemandFactor: 2,
            highLoadInput: neutralHighLoadInput
        );
        int second = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 100,
            engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0),
            log: null,
            dynamicMinimumParallelism: 4,
            allowScaleUp: true,
            scaleUpDemandFactor: 2,
            highLoadInput: neutralHighLoadInput
        );

        Assert.That(first, Is.EqualTo(4));
        Assert.That(second, Is.EqualTo(4));
    }

    [Test]
    public void EvaluateNext_回復帯が継続した時だけ段階復帰する()
    {
        ThumbnailParallelController controller = new(initialParallelism: 4);
        ThumbnailHighLoadInput recoveryWindowInput = new(
            batchProcessedCount: 10,
            batchFailedCount: 0,
            batchElapsedMs: 5000,
            queueActiveCount: 100,
            currentParallelism: 4,
            configuredParallelism: 8,
            hasSlowDemand: false,
            hasRecoveryDemand: false,
            engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0)
        );

        SetPrivateDateTimeField(controller, "lastScaleDownUtc", DateTime.UtcNow.AddMinutes(-10));

        int first = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 100,
            engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0),
            log: null,
            dynamicMinimumParallelism: 4,
            allowScaleUp: true,
            scaleUpDemandFactor: 2,
            highLoadInput: recoveryWindowInput
        );
        int second = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 100,
            engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0),
            log: null,
            dynamicMinimumParallelism: 4,
            allowScaleUp: true,
            scaleUpDemandFactor: 2,
            highLoadInput: recoveryWindowInput
        );

        Assert.That(first, Is.EqualTo(4));
        Assert.That(second, Is.EqualTo(5));
    }

    [Test]
    public void EvaluateNext_減速直後ブロック中は回復帯でも復帰しない()
    {
        ThumbnailParallelController controller = new(initialParallelism: 4);
        ThumbnailHighLoadInput recoveryWindowInput = new(
            batchProcessedCount: 10,
            batchFailedCount: 0,
            batchElapsedMs: 5000,
            queueActiveCount: 100,
            currentParallelism: 4,
            configuredParallelism: 8,
            hasSlowDemand: false,
            hasRecoveryDemand: false,
            engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0)
        );

        SetPrivateDateTimeField(controller, "lastScaleDownUtc", DateTime.UtcNow.AddSeconds(-5));

        int first = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 100,
            engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0),
            log: null,
            dynamicMinimumParallelism: 4,
            allowScaleUp: true,
            scaleUpDemandFactor: 2,
            highLoadInput: recoveryWindowInput
        );
        int second = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 100,
            engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0),
            log: null,
            dynamicMinimumParallelism: 4,
            allowScaleUp: true,
            scaleUpDemandFactor: 2,
            highLoadInput: recoveryWindowInput
        );

        Assert.That(first, Is.EqualTo(4));
        Assert.That(second, Is.EqualTo(4));
    }

    [Test]
    public void EvaluateNext_復帰クールダウン中は安定窓が揃っても連続復帰しない()
    {
        ThumbnailParallelController controller = new(initialParallelism: 4);
        ThumbnailHighLoadInput recoveryWindowInput = new(
            batchProcessedCount: 10,
            batchFailedCount: 0,
            batchElapsedMs: 5000,
            queueActiveCount: 100,
            currentParallelism: 4,
            configuredParallelism: 8,
            hasSlowDemand: false,
            hasRecoveryDemand: false,
            engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0)
        );

        SetPrivateDateTimeField(controller, "lastScaleDownUtc", DateTime.UtcNow.AddMinutes(-10));

        int first = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 100,
            engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0),
            log: null,
            dynamicMinimumParallelism: 4,
            allowScaleUp: true,
            scaleUpDemandFactor: 2,
            highLoadInput: recoveryWindowInput
        );
        int second = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 100,
            engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0),
            log: null,
            dynamicMinimumParallelism: 4,
            allowScaleUp: true,
            scaleUpDemandFactor: 2,
            highLoadInput: recoveryWindowInput
        );
        int third = controller.EvaluateNext(
            configuredParallelism: 8,
            batchProcessedCount: 10,
            batchFailedCount: 0,
            queueActiveCount: 100,
            engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0),
            log: null,
            dynamicMinimumParallelism: 4,
            allowScaleUp: true,
            scaleUpDemandFactor: 2,
            highLoadInput: recoveryWindowInput
        );

        Assert.That(first, Is.EqualTo(4));
        Assert.That(second, Is.EqualTo(5));
        Assert.That(third, Is.EqualTo(5));
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
