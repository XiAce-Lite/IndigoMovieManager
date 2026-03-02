namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// ホスト側から使うEverything連携の統一窓口。
    /// mode判定とfallback方針をここへ集約する。
    /// </summary>
    internal sealed class IndexProviderFacade : IIndexProviderFacade
    {
        private readonly IFileIndexProvider _provider;

        public IndexProviderFacade(IFileIndexProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public bool IsIntegrationConfigured(IntegrationMode mode)
        {
            return mode != IntegrationMode.Off;
        }

        public AvailabilityResult CheckAvailability(IntegrationMode mode)
        {
            if (!IsIntegrationConfigured(mode))
            {
                return new AvailabilityResult(false, EverythingReasonCodes.SettingDisabled);
            }

            AvailabilityResult providerResult = _provider.CheckAvailability();
            if (providerResult.CanUse)
            {
                return providerResult;
            }

            // AUTO時だけ unavailable を専用reasonへ丸める。
            if (
                mode == IntegrationMode.Auto
                && string.Equals(
                    providerResult.Reason,
                    EverythingReasonCodes.EverythingNotAvailable,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return new AvailabilityResult(false, EverythingReasonCodes.AutoNotAvailable);
            }

            return new AvailabilityResult(
                providerResult.CanUse,
                NormalizeReasonByMode(mode, providerResult.Reason)
            );
        }

        public ScanByProviderResult CollectMoviePathsWithFallback(
            FileIndexQueryOptions options,
            IntegrationMode mode
        )
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            AvailabilityResult availability = CheckAvailability(mode);
            if (!availability.CanUse)
            {
                return new ScanByProviderResult(
                    FileIndexStrategies.Filesystem,
                    availability.Reason,
                    [],
                    null
                );
            }

            FileIndexMovieResult providerResult = _provider.CollectMoviePaths(options);
            if (providerResult.Success)
            {
                return new ScanByProviderResult(
                    FileIndexStrategies.Everything,
                    providerResult.Reason,
                    providerResult.MoviePaths,
                    providerResult.MaxObservedChangedUtc
                );
            }

            return new ScanByProviderResult(
                FileIndexStrategies.Filesystem,
                NormalizeReasonByMode(mode, providerResult.Reason),
                [],
                null
            );
        }

        public FileIndexThumbnailBodyResult CollectThumbnailBodiesWithFallback(
            string thumbFolder,
            IntegrationMode mode
        )
        {
            if (string.IsNullOrWhiteSpace(thumbFolder))
            {
                throw new ArgumentException("thumbFolder is required.", nameof(thumbFolder));
            }

            AvailabilityResult availability = CheckAvailability(mode);
            if (!availability.CanUse)
            {
                return new FileIndexThumbnailBodyResult(
                    false,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    availability.Reason
                );
            }

            FileIndexThumbnailBodyResult providerResult = _provider.CollectThumbnailBodies(
                thumbFolder
            );
            return new FileIndexThumbnailBodyResult(
                providerResult.Success,
                providerResult.Bodies,
                NormalizeReasonByMode(mode, providerResult.Reason)
            );
        }

        private static string NormalizeReasonByMode(IntegrationMode mode, string reason)
        {
            if (
                mode == IntegrationMode.Auto
                && string.Equals(
                    reason,
                    EverythingReasonCodes.EverythingNotAvailable,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return EverythingReasonCodes.AutoNotAvailable;
            }

            return reason ?? "";
        }
    }
}
