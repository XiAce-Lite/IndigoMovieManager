using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Ipc;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class AdminTelemetryRuntimeResolverTests
{
    [Test]
    public async Task ResolveAsync_NoOpClientではinternal_onlyへ落とす()
    {
        AdminTelemetryRuntimeSnapshot actual = await AdminTelemetryRuntimeResolver.ResolveAsync(
            NoOpAdminTelemetryClient.Instance,
            AdminTelemetryRuntimeResolver.CreateThumbnailRequestContext("owner-1"),
            CreateInput(queueActiveCount: 9, hasSlowDemand: true, hasRecoveryDemand: false),
            "disk-00",
            "volume-00",
            CancellationToken.None
        );

        Assert.That(actual.Mode, Is.EqualTo(AdminTelemetryRuntimeMode.InternalOnly));
        Assert.That(
            actual.SystemLoadSource,
            Is.EqualTo(AdminTelemetrySignalSourceKind.Internal)
        );
        Assert.That(actual.FallbackKind, Is.EqualTo(AdminTelemetryFallbackKind.Unavailable));
        Assert.That(actual.FallbackReason, Is.EqualTo("no-client"));
        Assert.That(actual.SystemLoadSnapshot.QueueBacklogCount, Is.EqualTo(9));
        Assert.That(actual.SystemLoadSnapshot.SlowLaneBacklogCount, Is.EqualTo(1));
        Assert.That(actual.SystemLoadSnapshot.RecoveryLaneBacklogCount, Is.EqualTo(0));
        Assert.That(actual.DiskThermalSource, Is.EqualTo(AdminTelemetrySignalSourceKind.Internal));
        Assert.That(
            actual.DiskThermalFallbackKind,
            Is.EqualTo(AdminTelemetryFallbackKind.Unavailable)
        );
        Assert.That(
            actual.DiskThermalSnapshot.ThermalState,
            Is.EqualTo(DiskThermalState.Unavailable)
        );
        Assert.That(actual.UsnMftSource, Is.EqualTo(AdminTelemetrySignalSourceKind.Internal));
        Assert.That(
            actual.UsnMftFallbackKind,
            Is.EqualTo(AdminTelemetryFallbackKind.Unavailable)
        );
        Assert.That(actual.UsnMftStatus.StatusKind, Is.EqualTo(UsnMftStatusKind.Unavailable));
    }

    [Test]
    public async Task ResolveAsync_ServiceClientがSystemLoad対応ならserviceを使う()
    {
        ServiceReadyAdminTelemetryClient client = new();

        AdminTelemetryRuntimeSnapshot actual = await AdminTelemetryRuntimeResolver.ResolveAsync(
            client,
            AdminTelemetryRuntimeResolver.CreateThumbnailRequestContext("owner-2"),
            CreateInput(queueActiveCount: 4, hasSlowDemand: false, hasRecoveryDemand: true),
            "disk-01",
            "volume-01",
            CancellationToken.None
        );

        Assert.That(actual.Mode, Is.EqualTo(AdminTelemetryRuntimeMode.Service));
        Assert.That(
            actual.SystemLoadSource,
            Is.EqualTo(AdminTelemetrySignalSourceKind.Service)
        );
        Assert.That(actual.FallbackKind, Is.EqualTo(AdminTelemetryFallbackKind.None));
        Assert.That(actual.FallbackReason, Is.Empty);
        Assert.That(actual.SystemLoadSnapshot.CpuUsageRate, Is.EqualTo(0.42d));
        Assert.That(actual.DiskThermalSource, Is.EqualTo(AdminTelemetrySignalSourceKind.Service));
        Assert.That(actual.DiskThermalFallbackKind, Is.EqualTo(AdminTelemetryFallbackKind.None));
        Assert.That(
            actual.DiskThermalSnapshot.ThermalState,
            Is.EqualTo(DiskThermalState.Warning)
        );
        Assert.That(actual.UsnMftSource, Is.EqualTo(AdminTelemetrySignalSourceKind.Service));
        Assert.That(actual.UsnMftFallbackKind, Is.EqualTo(AdminTelemetryFallbackKind.None));
        Assert.That(actual.UsnMftStatus.StatusKind, Is.EqualTo(UsnMftStatusKind.Busy));
        Assert.That(client.CapabilityCallCount, Is.EqualTo(1));
        Assert.That(client.SystemLoadCallCount, Is.EqualTo(1));
        Assert.That(client.DiskThermalCallCount, Is.EqualTo(1));
        Assert.That(client.UsnMftCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ResolveAsync_ServiceClientが権限不足ならaccess_deniedへ落とす()
    {
        AccessDeniedAdminTelemetryClient client = new();

        AdminTelemetryRuntimeSnapshot actual = await AdminTelemetryRuntimeResolver.ResolveAsync(
            client,
            AdminTelemetryRuntimeResolver.CreateThumbnailRequestContext("owner-3"),
            CreateInput(queueActiveCount: 6, hasSlowDemand: false, hasRecoveryDemand: true),
            "disk-02",
            "volume-02",
            CancellationToken.None
        );

        Assert.That(actual.Mode, Is.EqualTo(AdminTelemetryRuntimeMode.InternalOnly));
        Assert.That(actual.FallbackKind, Is.EqualTo(AdminTelemetryFallbackKind.AccessDenied));
        Assert.That(actual.FallbackReason, Is.EqualTo("capabilities:UnauthorizedAccessException"));
        Assert.That(actual.SystemLoadSnapshot.QueueBacklogCount, Is.EqualTo(6));
        Assert.That(actual.SystemLoadSnapshot.RecoveryLaneBacklogCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ResolveAsync_ServiceClientがタイムアウトならtimeoutへ落とす()
    {
        TimeoutAdminTelemetryClient client = new();

        AdminTelemetryRuntimeSnapshot actual = await AdminTelemetryRuntimeResolver.ResolveAsync(
            client,
            AdminTelemetryRuntimeResolver.CreateThumbnailRequestContext("owner-5"),
            CreateInput(queueActiveCount: 7, hasSlowDemand: false, hasRecoveryDemand: false),
            "disk-03",
            "volume-03",
            CancellationToken.None
        );

        Assert.That(actual.Mode, Is.EqualTo(AdminTelemetryRuntimeMode.InternalOnly));
        Assert.That(actual.FallbackKind, Is.EqualTo(AdminTelemetryFallbackKind.Timeout));
        Assert.That(actual.FallbackReason, Is.EqualTo("capabilities"));
    }

    [Test]
    public async Task ResolveAsync_個別signalの失敗種別を保持する()
    {
        PartialFailureAdminTelemetryClient client = new();

        AdminTelemetryRuntimeSnapshot actual = await AdminTelemetryRuntimeResolver.ResolveAsync(
            client,
            AdminTelemetryRuntimeResolver.CreateThumbnailRequestContext("owner-6"),
            CreateInput(queueActiveCount: 5, hasSlowDemand: false, hasRecoveryDemand: true),
            "disk-04",
            "volume-04",
            CancellationToken.None
        );

        Assert.That(actual.Mode, Is.EqualTo(AdminTelemetryRuntimeMode.Service));
        Assert.That(actual.FallbackKind, Is.EqualTo(AdminTelemetryFallbackKind.None));
        Assert.That(actual.DiskThermalSource, Is.EqualTo(AdminTelemetrySignalSourceKind.Internal));
        Assert.That(
            actual.DiskThermalFallbackKind,
            Is.EqualTo(AdminTelemetryFallbackKind.AccessDenied)
        );
        Assert.That(
            actual.DiskThermalFallbackReason,
            Is.EqualTo("disk-thermal:UnauthorizedAccessException")
        );
        Assert.That(actual.UsnMftSource, Is.EqualTo(AdminTelemetrySignalSourceKind.Internal));
        Assert.That(actual.UsnMftFallbackKind, Is.EqualTo(AdminTelemetryFallbackKind.Timeout));
        Assert.That(actual.UsnMftFallbackReason, Is.EqualTo("usnmft"));
    }

    [Test]
    public void CreateThumbnailRequestContext_Thumbnail側既定値を返す()
    {
        AdminTelemetryRequestContext actual =
            AdminTelemetryRuntimeResolver.CreateThumbnailRequestContext("owner-4");

        Assert.That(
            actual.ConsumerKind,
            Is.EqualTo(AdminTelemetryConsumerKind.ThumbnailOrchestrator)
        );
        Assert.That(actual.OrchestratorInstanceId, Is.EqualTo("owner-4"));
        Assert.That(actual.CallerProcessId, Is.GreaterThan(0));
        Assert.That(actual.RequestedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    private static ThumbnailHighLoadInput CreateInput(
        int queueActiveCount,
        bool hasSlowDemand,
        bool hasRecoveryDemand
    )
    {
        return new ThumbnailHighLoadInput(
            batchProcessedCount: 8,
            batchFailedCount: 1,
            batchElapsedMs: 2500,
            queueActiveCount: queueActiveCount,
            currentParallelism: 4,
            configuredParallelism: 8,
            hasSlowDemand: hasSlowDemand,
            hasRecoveryDemand: hasRecoveryDemand,
            engineSnapshot: new ThumbnailEngineRuntimeSnapshot(0, 0, 0)
        );
    }

    private sealed class ServiceReadyAdminTelemetryClient : IAdminTelemetryClient
    {
        public int CapabilityCallCount { get; private set; }
        public int SystemLoadCallCount { get; private set; }
        public int DiskThermalCallCount { get; private set; }
        public int UsnMftCallCount { get; private set; }

        public Task<AdminTelemetryServiceCapabilities> GetCapabilitiesAsync(
            AdminTelemetryRequestContext requestContext,
            CancellationToken cancellationToken
        )
        {
            CapabilityCallCount++;
            return Task.FromResult(
                new AdminTelemetryServiceCapabilities
                {
                    ServiceVersion = "test-service",
                    RequiresElevation = true,
                    SupportsSystemLoad = true,
                    SupportsDiskThermal = true,
                    SupportsUsnMftStatus = true,
                    SupportsWatcherIntegration = true,
                }
            );
        }

        public Task<SystemLoadSnapshotDto> GetSystemLoadSnapshotAsync(
            AdminTelemetryRequestContext requestContext,
            CancellationToken cancellationToken
        )
        {
            SystemLoadCallCount++;
            return Task.FromResult(
                new SystemLoadSnapshotDto
                {
                    CpuUsageRate = 0.42d,
                    IoBusyRate = 0.18d,
                    MemoryPressureRate = 0.21d,
                    QueueBacklogCount = 11,
                    SlowLaneBacklogCount = 2,
                    RecoveryLaneBacklogCount = 1,
                    SampleWindowMs = 2000,
                    CapturedAtUtc = DateTime.UtcNow,
                }
            );
        }

        public Task<DiskThermalSnapshotDto> GetDiskThermalSnapshotAsync(
            AdminTelemetryRequestContext requestContext,
            string diskId,
            CancellationToken cancellationToken
        )
        {
            DiskThermalCallCount++;
            return Task.FromResult(
                new DiskThermalSnapshotDto
                {
                    DiskId = diskId,
                    TemperatureCelsius = 57,
                    WarningThresholdCelsius = 55,
                    CriticalThresholdCelsius = 65,
                    ThermalState = DiskThermalState.Warning,
                    CapturedAtUtc = DateTime.UtcNow,
                }
            );
        }

        public Task<UsnMftStatusDto> GetUsnMftStatusAsync(
            AdminTelemetryRequestContext requestContext,
            string volumeName,
            CancellationToken cancellationToken
        )
        {
            UsnMftCallCount++;
            return Task.FromResult(
                new UsnMftStatusDto
                {
                    VolumeName = volumeName,
                    Available = true,
                    LastScanLatencyMs = 6400,
                    JournalBacklogCount = 18,
                    StatusKind = UsnMftStatusKind.Busy,
                    CapturedAtUtc = DateTime.UtcNow,
                }
            );
        }
    }

    private sealed class AccessDeniedAdminTelemetryClient : IAdminTelemetryClient
    {
        public Task<AdminTelemetryServiceCapabilities> GetCapabilitiesAsync(
            AdminTelemetryRequestContext requestContext,
            CancellationToken cancellationToken
        )
        {
            throw new UnauthorizedAccessException("access denied");
        }

        public Task<SystemLoadSnapshotDto> GetSystemLoadSnapshotAsync(
            AdminTelemetryRequestContext requestContext,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<DiskThermalSnapshotDto> GetDiskThermalSnapshotAsync(
            AdminTelemetryRequestContext requestContext,
            string diskId,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<UsnMftStatusDto> GetUsnMftStatusAsync(
            AdminTelemetryRequestContext requestContext,
            string volumeName,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TimeoutAdminTelemetryClient : IAdminTelemetryClient
    {
        public async Task<AdminTelemetryServiceCapabilities> GetCapabilitiesAsync(
            AdminTelemetryRequestContext requestContext,
            CancellationToken cancellationToken
        )
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new AdminTelemetryServiceCapabilities();
        }

        public Task<SystemLoadSnapshotDto> GetSystemLoadSnapshotAsync(
            AdminTelemetryRequestContext requestContext,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<DiskThermalSnapshotDto> GetDiskThermalSnapshotAsync(
            AdminTelemetryRequestContext requestContext,
            string diskId,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }

        public Task<UsnMftStatusDto> GetUsnMftStatusAsync(
            AdminTelemetryRequestContext requestContext,
            string volumeName,
            CancellationToken cancellationToken
        )
        {
            throw new NotSupportedException();
        }
    }

    private sealed class PartialFailureAdminTelemetryClient : IAdminTelemetryClient
    {
        public Task<AdminTelemetryServiceCapabilities> GetCapabilitiesAsync(
            AdminTelemetryRequestContext requestContext,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
                new AdminTelemetryServiceCapabilities
                {
                    ServiceVersion = "partial-failure-service",
                    RequiresElevation = true,
                    SupportsSystemLoad = true,
                    SupportsDiskThermal = true,
                    SupportsUsnMftStatus = true,
                    SupportsWatcherIntegration = true,
                }
            );
        }

        public Task<SystemLoadSnapshotDto> GetSystemLoadSnapshotAsync(
            AdminTelemetryRequestContext requestContext,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
                new SystemLoadSnapshotDto
                {
                    CpuUsageRate = 0.50d,
                    IoBusyRate = 0.25d,
                    MemoryPressureRate = 0.30d,
                    QueueBacklogCount = 9,
                    SlowLaneBacklogCount = 0,
                    RecoveryLaneBacklogCount = 1,
                    SampleWindowMs = 1800,
                    CapturedAtUtc = DateTime.UtcNow,
                }
            );
        }

        public Task<DiskThermalSnapshotDto> GetDiskThermalSnapshotAsync(
            AdminTelemetryRequestContext requestContext,
            string diskId,
            CancellationToken cancellationToken
        )
        {
            throw new UnauthorizedAccessException("access denied");
        }

        public async Task<UsnMftStatusDto> GetUsnMftStatusAsync(
            AdminTelemetryRequestContext requestContext,
            string volumeName,
            CancellationToken cancellationToken
        )
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new UsnMftStatusDto();
        }
    }
}
