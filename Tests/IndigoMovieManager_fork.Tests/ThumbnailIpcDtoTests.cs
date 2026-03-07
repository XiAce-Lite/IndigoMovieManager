using System.Reflection;
using IndigoMovieManager.Thumbnail.Ipc;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailIpcDtoTests
{
    [Test]
    public void EnumNames_仕様どおりに固定する()
    {
        Assert.That(
            Enum.GetNames<EngineJobLaneKind>(),
            Is.EqualTo(new[] { "Normal", "Slow", "Recovery" })
        );
        Assert.That(
            Enum.GetNames<EngineFailureKind>(),
            Is.EqualTo(new[] { "None", "Io", "Decode", "Index", "Timeout", "Unknown" })
        );
        Assert.That(
            Enum.GetNames<DiskThermalState>(),
            Is.EqualTo(new[] { "Normal", "Warning", "Critical", "Unavailable" })
        );
        Assert.That(
            Enum.GetNames<UsnMftStatusKind>(),
            Is.EqualTo(new[] { "Ready", "Busy", "Unavailable", "AccessDenied" })
        );
        Assert.That(
            Enum.GetNames<ThrottleDecisionKind>(),
            Is.EqualTo(new[] { "Keep", "ThrottleDown", "RecoverUp" })
        );
        Assert.That(
            Enum.GetNames<ThrottleReasonKind>(),
            Is.EqualTo(new[] { "Error", "HighLoad", "Thermal", "Manual", "Fallback" })
        );
    }

    [Test]
    public void DtoPropertyNames_仕様どおりに固定する()
    {
        Assert.That(
            GetPublicPropertyNames(typeof(EngineJobMetricsDto)),
            Is.EqualTo(
                new[]
                {
                    "JobId",
                    "SourcePath",
                    "LaneKind",
                    "ElapsedMs",
                    "Succeeded",
                    "FailureKind",
                    "AttemptCount",
                    "DecodedFrameCount",
                    "PeakWorkingSetMb",
                    "CapturedAtUtc",
                }
            )
        );
        Assert.That(
            GetPublicPropertyNames(typeof(SystemLoadSnapshotDto)),
            Is.EqualTo(
                new[]
                {
                    "CpuUsageRate",
                    "IoBusyRate",
                    "MemoryPressureRate",
                    "QueueBacklogCount",
                    "SlowLaneBacklogCount",
                    "RecoveryLaneBacklogCount",
                    "SampleWindowMs",
                    "CapturedAtUtc",
                }
            )
        );
        Assert.That(
            GetPublicPropertyNames(typeof(DiskThermalSnapshotDto)),
            Is.EqualTo(
                new[]
                {
                    "DiskId",
                    "TemperatureCelsius",
                    "WarningThresholdCelsius",
                    "CriticalThresholdCelsius",
                    "ThermalState",
                    "CapturedAtUtc",
                }
            )
        );
        Assert.That(
            GetPublicPropertyNames(typeof(UsnMftStatusDto)),
            Is.EqualTo(
                new[]
                {
                    "VolumeName",
                    "Available",
                    "LastScanLatencyMs",
                    "JournalBacklogCount",
                    "StatusKind",
                    "CapturedAtUtc",
                }
            )
        );
        Assert.That(
            GetPublicPropertyNames(typeof(ThrottleDecisionDto)),
            Is.EqualTo(
                new[]
                {
                    "ConfiguredParallelism",
                    "EffectiveParallelism",
                    "DecisionKind",
                    "ReasonKind",
                    "ReasonDetail",
                    "CooldownUntilUtc",
                    "CapturedAtUtc",
                }
            )
        );
    }

    [Test]
    public void CapturedAtUtc_ローカル時刻入力でもUtcへ正規化する()
    {
        DateTime localTime = DateTime.SpecifyKind(
            new DateTime(2026, 3, 6, 21, 15, 0),
            DateTimeKind.Local
        );

        EngineJobMetricsDto dto = new() { CapturedAtUtc = localTime };

        Assert.That(dto.CapturedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(dto.CapturedAtUtc, Is.EqualTo(localTime.ToUniversalTime()));
    }

    [Test]
    public void ThrottleDecisionDto_復帰待ち時刻もUtcへ正規化する()
    {
        DateTime localTime = DateTime.SpecifyKind(
            new DateTime(2026, 3, 6, 22, 30, 0),
            DateTimeKind.Local
        );

        ThrottleDecisionDto dto = new()
        {
            CooldownUntilUtc = localTime,
            CapturedAtUtc = localTime,
        };

        Assert.That(dto.CooldownUntilUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(dto.CooldownUntilUtc, Is.EqualTo(localTime.ToUniversalTime()));
        Assert.That(dto.CapturedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public void 状態系Dto_未取得時はUnavailableを既定にする()
    {
        DiskThermalSnapshotDto thermal = new();
        UsnMftStatusDto usnMft = new();

        Assert.That(thermal.ThermalState, Is.EqualTo(DiskThermalState.Unavailable));
        Assert.That(usnMft.StatusKind, Is.EqualTo(UsnMftStatusKind.Unavailable));
    }

    private static string[] GetPublicPropertyNames(Type type)
    {
        return
        [
            .. type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(x => x.Name),
        ];
    }
}
