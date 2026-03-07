using System.Diagnostics;
using System.ComponentModel;

namespace IndigoMovieManager.Thumbnail.Ipc
{
    public enum AdminTelemetryRuntimeMode
    {
        InternalOnly = 0,
        Service = 1,
    }

    public enum AdminTelemetrySignalSourceKind
    {
        Internal = 0,
        Service = 1,
    }

    public enum AdminTelemetryFallbackKind
    {
        None = 0,
        Unavailable = 1,
        AccessDenied = 2,
        Timeout = 3,
    }

    // 現時点の管理者権限サービス接続状態と、SystemLoadの採用元をまとめる。
    public sealed record AdminTelemetryRuntimeSnapshot
    {
        public AdminTelemetryRuntimeMode Mode { get; init; } = AdminTelemetryRuntimeMode.InternalOnly;
        public AdminTelemetrySignalSourceKind SystemLoadSource { get; init; } =
            AdminTelemetrySignalSourceKind.Internal;
        public AdminTelemetrySignalSourceKind DiskThermalSource { get; init; } =
            AdminTelemetrySignalSourceKind.Internal;
        public AdminTelemetrySignalSourceKind UsnMftSource { get; init; } =
            AdminTelemetrySignalSourceKind.Internal;
        public AdminTelemetryFallbackKind FallbackKind { get; init; } =
            AdminTelemetryFallbackKind.Unavailable;
        public string FallbackReason { get; init; } = "internal-only";
        public AdminTelemetryFallbackKind DiskThermalFallbackKind { get; init; } =
            AdminTelemetryFallbackKind.Unavailable;
        public string DiskThermalFallbackReason { get; init; } = "internal-only";
        public AdminTelemetryFallbackKind UsnMftFallbackKind { get; init; } =
            AdminTelemetryFallbackKind.Unavailable;
        public string UsnMftFallbackReason { get; init; } = "internal-only";
        public AdminTelemetryServiceCapabilities Capabilities { get; init; } = new();
        public SystemLoadSnapshotDto SystemLoadSnapshot { get; init; } = new();
        public DiskThermalSnapshotDto DiskThermalSnapshot { get; init; } = new();
        public UsnMftStatusDto UsnMftStatus { get; init; } = new();
    }

    // 未接続でも常に呼べる既定クライアント。
    // ここでは例外を投げず、resolver側で internal-only へ素直に落とせる形を返す。
    public sealed class NoOpAdminTelemetryClient : IAdminTelemetryClient
    {
        public static readonly NoOpAdminTelemetryClient Instance = new();

        private NoOpAdminTelemetryClient() { }

        public Task<AdminTelemetryServiceCapabilities> GetCapabilitiesAsync(
            AdminTelemetryRequestContext requestContext,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
                new AdminTelemetryServiceCapabilities
                {
                    ServiceVersion = "internal-only",
                    RequiresElevation = false,
                    SupportsSystemLoad = false,
                    SupportsDiskThermal = false,
                    SupportsUsnMftStatus = false,
                    SupportsWatcherIntegration = true,
                }
            );
        }

        public Task<SystemLoadSnapshotDto> GetSystemLoadSnapshotAsync(
            AdminTelemetryRequestContext requestContext,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(new SystemLoadSnapshotDto());
        }

        public Task<DiskThermalSnapshotDto> GetDiskThermalSnapshotAsync(
            AdminTelemetryRequestContext requestContext,
            string diskId,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(new DiskThermalSnapshotDto());
        }

        public Task<UsnMftStatusDto> GetUsnMftStatusAsync(
            AdminTelemetryRequestContext requestContext,
            string volumeName,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(new UsnMftStatusDto());
        }
    }

    // サービス接続の有無にかかわらず、オーケストレータ側で同じ形の負荷スナップショットを得る。
    public static class AdminTelemetryRuntimeResolver
    {
        public static async Task<AdminTelemetryRuntimeSnapshot> ResolveAsync(
            IAdminTelemetryClient adminTelemetryClient,
            AdminTelemetryRequestContext requestContext,
            ThumbnailHighLoadInput internalHighLoadInput,
            string diskId,
            string volumeName,
            CancellationToken cancellationToken
        )
        {
            IAdminTelemetryClient safeClient = adminTelemetryClient ?? NoOpAdminTelemetryClient.Instance;
            if (ReferenceEquals(safeClient, NoOpAdminTelemetryClient.Instance))
            {
                return CreateInternalOnlySnapshot(
                    internalHighLoadInput,
                    AdminTelemetryFallbackKind.Unavailable,
                    "no-client",
                    new AdminTelemetryServiceCapabilities
                    {
                        ServiceVersion = "internal-only",
                        RequiresElevation = false,
                        SupportsSystemLoad = false,
                        SupportsDiskThermal = false,
                        SupportsUsnMftStatus = false,
                        SupportsWatcherIntegration = true,
                    }
                );
            }

            AdminTelemetryServiceCapabilities capabilities;

            try
            {
                capabilities = await ExecuteWithTimeoutAsync(
                    token => safeClient.GetCapabilitiesAsync(requestContext, token),
                    TimeSpan.FromMilliseconds(ThumbnailIpcTransportPolicy.HealthCheckTimeoutMs),
                    cancellationToken
                ).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return CreateInternalOnlySnapshot(
                    internalHighLoadInput,
                    AdminTelemetryFallbackKind.Timeout,
                    "capabilities",
                    new AdminTelemetryServiceCapabilities { ServiceVersion = "timeout" }
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                (AdminTelemetryFallbackKind fallbackKind, string fallbackReason) =
                    ClassifyFailure(ex, "capabilities");
                return CreateInternalOnlySnapshot(
                    internalHighLoadInput,
                    fallbackKind,
                    fallbackReason,
                    new AdminTelemetryServiceCapabilities { ServiceVersion = ex.GetType().Name }
                );
            }

            if (!capabilities.SupportsSystemLoad)
            {
                return CreateInternalOnlySnapshot(
                    internalHighLoadInput,
                    AdminTelemetryFallbackKind.Unavailable,
                    "unsupported-system-load",
                    capabilities
                );
            }

            try
            {
                SystemLoadSnapshotDto systemLoadSnapshot = await ExecuteWithTimeoutAsync(
                    token => safeClient.GetSystemLoadSnapshotAsync(requestContext, token),
                    TimeSpan.FromMilliseconds(ThumbnailIpcTransportPolicy.RequestTimeoutMs),
                    cancellationToken
                ).ConfigureAwait(false);
                DiskThermalSnapshotDto diskThermalSnapshot = new();
                AdminTelemetrySignalSourceKind diskThermalSource =
                    AdminTelemetrySignalSourceKind.Internal;
                AdminTelemetryFallbackKind diskThermalFallbackKind =
                    capabilities.SupportsDiskThermal
                        ? AdminTelemetryFallbackKind.None
                        : AdminTelemetryFallbackKind.Unavailable;
                string diskThermalFallbackReason = capabilities.SupportsDiskThermal
                    ? ""
                    : "unsupported";
                UsnMftStatusDto usnMftStatus = new();
                AdminTelemetrySignalSourceKind usnMftSource =
                    AdminTelemetrySignalSourceKind.Internal;
                AdminTelemetryFallbackKind usnMftFallbackKind =
                    capabilities.SupportsUsnMftStatus
                        ? AdminTelemetryFallbackKind.None
                        : AdminTelemetryFallbackKind.Unavailable;
                string usnMftFallbackReason = capabilities.SupportsUsnMftStatus
                    ? ""
                    : "unsupported";
                if (capabilities.SupportsDiskThermal)
                {
                    try
                    {
                        diskThermalSnapshot = await ExecuteWithTimeoutAsync(
                            token =>
                                safeClient.GetDiskThermalSnapshotAsync(
                                    requestContext,
                                    diskId ?? "",
                                    token
                                ),
                            TimeSpan.FromMilliseconds(ThumbnailIpcTransportPolicy.RequestTimeoutMs),
                            cancellationToken
                        ).ConfigureAwait(false);
                        diskThermalSource = AdminTelemetrySignalSourceKind.Service;
                        diskThermalFallbackKind = AdminTelemetryFallbackKind.None;
                        diskThermalFallbackReason = "";
                    }
                    catch (TimeoutException)
                    {
                        diskThermalSnapshot = new DiskThermalSnapshotDto();
                        diskThermalFallbackKind = AdminTelemetryFallbackKind.Timeout;
                        diskThermalFallbackReason = "disk-thermal";
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        diskThermalSnapshot = new DiskThermalSnapshotDto();
                        (
                            diskThermalFallbackKind,
                            diskThermalFallbackReason
                        ) = ClassifyFailure(ex, "disk-thermal");
                    }
                }
                if (capabilities.SupportsUsnMftStatus)
                {
                    try
                    {
                        usnMftStatus = await ExecuteWithTimeoutAsync(
                            token =>
                                safeClient.GetUsnMftStatusAsync(
                                    requestContext,
                                    volumeName ?? "",
                                    token
                                ),
                            TimeSpan.FromMilliseconds(ThumbnailIpcTransportPolicy.RequestTimeoutMs),
                            cancellationToken
                        ).ConfigureAwait(false);
                        usnMftSource = AdminTelemetrySignalSourceKind.Service;
                        usnMftFallbackKind = AdminTelemetryFallbackKind.None;
                        usnMftFallbackReason = "";
                    }
                    catch (TimeoutException)
                    {
                        usnMftStatus = new UsnMftStatusDto();
                        usnMftFallbackKind = AdminTelemetryFallbackKind.Timeout;
                        usnMftFallbackReason = "usnmft";
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        usnMftStatus = new UsnMftStatusDto();
                        (usnMftFallbackKind, usnMftFallbackReason) = ClassifyFailure(
                            ex,
                            "usnmft"
                        );
                    }
                }

                return new AdminTelemetryRuntimeSnapshot
                {
                    Mode = AdminTelemetryRuntimeMode.Service,
                    SystemLoadSource = AdminTelemetrySignalSourceKind.Service,
                    DiskThermalSource = diskThermalSource,
                    UsnMftSource = usnMftSource,
                    FallbackKind = AdminTelemetryFallbackKind.None,
                    FallbackReason = "",
                    DiskThermalFallbackKind = diskThermalFallbackKind,
                    DiskThermalFallbackReason = diskThermalFallbackReason,
                    UsnMftFallbackKind = usnMftFallbackKind,
                    UsnMftFallbackReason = usnMftFallbackReason,
                    Capabilities = capabilities,
                    SystemLoadSnapshot = systemLoadSnapshot,
                    DiskThermalSnapshot = diskThermalSnapshot,
                    UsnMftStatus = usnMftStatus,
                };
            }
            catch (TimeoutException)
            {
                return CreateInternalOnlySnapshot(
                    internalHighLoadInput,
                    AdminTelemetryFallbackKind.Timeout,
                    "system-load",
                    capabilities
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                (AdminTelemetryFallbackKind fallbackKind, string fallbackReason) =
                    ClassifyFailure(ex, "system-load");
                return CreateInternalOnlySnapshot(
                    internalHighLoadInput,
                    fallbackKind,
                    fallbackReason,
                    capabilities
                );
            }
        }

        // 内部メトリクス版でも、後段のDTO受け口は同じ形に揃える。
        public static SystemLoadSnapshotDto CreateInternalSystemLoadSnapshot(
            ThumbnailHighLoadInput input
        )
        {
            return new SystemLoadSnapshotDto
            {
                CpuUsageRate = 0.0d,
                IoBusyRate = 0.0d,
                MemoryPressureRate = 0.0d,
                QueueBacklogCount = Math.Max(0, input.QueueActiveCount),
                SlowLaneBacklogCount = input.HasSlowDemand ? 1 : 0,
                RecoveryLaneBacklogCount = input.HasRecoveryDemand ? 1 : 0,
                SampleWindowMs = Math.Max(0L, input.BatchElapsedMs),
                CapturedAtUtc = DateTime.UtcNow,
            };
        }

        public static AdminTelemetryRequestContext CreateThumbnailRequestContext(
            string ownerInstanceId
        )
        {
            Process current = Process.GetCurrentProcess();
            return new AdminTelemetryRequestContext
            {
                ConsumerKind = AdminTelemetryConsumerKind.ThumbnailOrchestrator,
                OrchestratorInstanceId = ownerInstanceId ?? "",
                CallerProcessName = current.ProcessName ?? "",
                CallerProcessId = current.Id,
                RequestedAtUtc = DateTime.UtcNow,
            };
        }

        private static AdminTelemetryRuntimeSnapshot CreateInternalOnlySnapshot(
            ThumbnailHighLoadInput internalHighLoadInput,
            AdminTelemetryFallbackKind fallbackKind,
            string fallbackReason,
            AdminTelemetryServiceCapabilities capabilities
        )
        {
            return new AdminTelemetryRuntimeSnapshot
            {
                Mode = AdminTelemetryRuntimeMode.InternalOnly,
                SystemLoadSource = AdminTelemetrySignalSourceKind.Internal,
                FallbackKind = fallbackKind,
                FallbackReason = string.IsNullOrWhiteSpace(fallbackReason)
                    ? "internal-only"
                    : fallbackReason,
                DiskThermalFallbackKind = fallbackKind,
                DiskThermalFallbackReason = string.IsNullOrWhiteSpace(fallbackReason)
                    ? "internal-only"
                    : fallbackReason,
                UsnMftFallbackKind = fallbackKind,
                UsnMftFallbackReason = string.IsNullOrWhiteSpace(fallbackReason)
                    ? "internal-only"
                    : fallbackReason,
                Capabilities = capabilities ?? new AdminTelemetryServiceCapabilities(),
                SystemLoadSnapshot = CreateInternalSystemLoadSnapshot(internalHighLoadInput),
                DiskThermalSnapshot = new DiskThermalSnapshotDto(),
                UsnMftStatus = new UsnMftStatusDto(),
            };
        }

        private static async Task<T> ExecuteWithTimeoutAsync<T>(
            Func<CancellationToken, Task<T>> action,
            TimeSpan timeout,
            CancellationToken cancellationToken
        )
        {
            using CancellationTokenSource linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(timeout);

            try
            {
                return await action(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException();
            }
        }

        // 権限不足だけは unavailable に埋もれさせず、後段ログで機械判定しやすい分類へ固定する。
        private static (AdminTelemetryFallbackKind Kind, string Reason) ClassifyFailure(
            Exception ex,
            string operation
        )
        {
            if (IsAccessDenied(ex))
            {
                return (
                    AdminTelemetryFallbackKind.AccessDenied,
                    $"{operation}:{ex.GetType().Name}"
                );
            }

            return (AdminTelemetryFallbackKind.Unavailable, $"{operation}:{ex.GetType().Name}");
        }

        private static bool IsAccessDenied(Exception ex)
        {
            if (ex is UnauthorizedAccessException)
            {
                return true;
            }

            if (ex is Win32Exception win32 && win32.NativeErrorCode == 5)
            {
                return true;
            }

            return ex.Message?.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
