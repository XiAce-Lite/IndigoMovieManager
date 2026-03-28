namespace IndigoMovieManager.Thumbnail.Ipc
{
    public enum AdminTelemetryConsumerKind
    {
        ThumbnailOrchestrator = 0,
        WatcherFacade = 1,
    }

    // 管理者権限サービスへ渡す呼び出し文脈。
    // 並列制御の最終判断はサービスへ渡さず、呼び出し元識別だけを持たせる。
    public sealed record AdminTelemetryRequestContext
    {
        private DateTime requestedAtUtc = DateTime.UtcNow;

        public AdminTelemetryConsumerKind ConsumerKind { get; init; } =
            AdminTelemetryConsumerKind.ThumbnailOrchestrator;
        public string OrchestratorInstanceId { get; init; } = "";
        public string CallerProcessName { get; init; } = "";
        public int CallerProcessId { get; init; }
        public DateTime RequestedAtUtc
        {
            get => requestedAtUtc;
            init => requestedAtUtc = ThumbnailIpcDateTimeNormalizer.NormalizeUtc(value);
        }
    }

    // サービスが何を返せるかを先に開示し、未接続や未実装を呼び出し側で分岐できるようにする。
    public sealed record AdminTelemetryServiceCapabilities
    {
        private DateTime capturedAtUtc = DateTime.UtcNow;

        public string ServiceVersion { get; init; } = "";
        public bool RequiresElevation { get; init; } = true;
        public bool SupportsSystemLoad { get; init; }
        public bool SupportsDiskThermal { get; init; }
        public bool SupportsUsnMftStatus { get; init; }
        public bool SupportsWatcherIntegration { get; init; }
        public DateTime CapturedAtUtc
        {
            get => capturedAtUtc;
            init => capturedAtUtc = ThumbnailIpcDateTimeNormalizer.NormalizeUtc(value);
        }
    }

    // 管理者権限サービスは「特権取得の代理人」に限定し、縮退判断やUI文言生成を持たない。
    public interface IAdminTelemetryClient
    {
        Task<AdminTelemetryServiceCapabilities> GetCapabilitiesAsync(
            AdminTelemetryRequestContext requestContext,
            CancellationToken cancellationToken
        );

        Task<SystemLoadSnapshotDto> GetSystemLoadSnapshotAsync(
            AdminTelemetryRequestContext requestContext,
            CancellationToken cancellationToken
        );

        Task<DiskThermalSnapshotDto> GetDiskThermalSnapshotAsync(
            AdminTelemetryRequestContext requestContext,
            string diskId,
            CancellationToken cancellationToken
        );

        Task<UsnMftStatusDto> GetUsnMftStatusAsync(
            AdminTelemetryRequestContext requestContext,
            string volumeName,
            CancellationToken cancellationToken
        );
    }
}
