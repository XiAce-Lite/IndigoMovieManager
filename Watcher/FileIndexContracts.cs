namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// Everything連携の動作モード（OFF/AUTO/ON）。
    /// モード判定はFacade側で一元管理する。
    /// </summary>
    internal enum IntegrationMode
    {
        Off = 0,
        Auto = 1,
        On = 2,
    }

    /// <summary>
    /// 動画候補収集の問い合わせ条件。
    /// </summary>
    internal sealed class FileIndexQueryOptions
    {
        public required string RootPath { get; init; }
        public bool IncludeSubdirectories { get; init; }
        public string CheckExt { get; init; } = "";
        public DateTime? ChangedSinceUtc { get; init; }
    }

    /// <summary>
    /// 可用性判定結果。
    /// </summary>
    internal sealed class AvailabilityResult
    {
        public AvailabilityResult(bool canUse, string reason)
        {
            CanUse = canUse;
            Reason = reason ?? "";
        }

        public bool CanUse { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// 動画候補収集結果。
    /// </summary>
    internal sealed class FileIndexMovieResult
    {
        public FileIndexMovieResult(
            bool success,
            List<string> moviePaths,
            DateTime? maxObservedChangedUtc,
            string reason
        )
        {
            Success = success;
            MoviePaths = moviePaths ?? [];
            MaxObservedChangedUtc = maxObservedChangedUtc;
            Reason = reason ?? "";
        }

        public bool Success { get; }
        public List<string> MoviePaths { get; }
        public DateTime? MaxObservedChangedUtc { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// サムネイルBody収集結果。
    /// </summary>
    internal sealed class FileIndexThumbnailBodyResult
    {
        public FileIndexThumbnailBodyResult(bool success, HashSet<string> bodies, string reason)
        {
            Success = success;
            Bodies = bodies ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Reason = reason ?? "";
        }

        public bool Success { get; }
        public HashSet<string> Bodies { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// Facade経由の動画候補収集結果。
    /// strategyは既存互換で string を維持する。
    /// </summary>
    internal sealed class ScanByProviderResult
    {
        public ScanByProviderResult(
            string strategy,
            string reason,
            List<string> moviePaths,
            DateTime? maxObservedChangedUtc
        )
        {
            Strategy = strategy ?? FileIndexStrategies.Filesystem;
            Reason = reason ?? "";
            MoviePaths = moviePaths ?? [];
            MaxObservedChangedUtc = maxObservedChangedUtc;
        }

        public string Strategy { get; }
        public string Reason { get; }
        public List<string> MoviePaths { get; }
        public DateTime? MaxObservedChangedUtc { get; }
    }

    /// <summary>
    /// 既存通知やログとの互換を維持するため、strategy文字列を固定値で管理する。
    /// </summary>
    internal static class FileIndexStrategies
    {
        public const string Everything = "everything";
        public const string Filesystem = "filesystem";
    }

    /// <summary>
    /// reasonコードの固定値。
    /// 既存文字列との互換を維持するため、ここを唯一の定義点にする。
    /// </summary>
    internal static class EverythingReasonCodes
    {
        public const string SettingDisabled = "setting_disabled";
        public const string AutoNotAvailable = "auto_not_available";
        public const string EverythingNotAvailable = "everything_not_available";
        public const string Ok = "ok";
        public const string OkPrefix = "ok:";
        public const string AvailabilityErrorPrefix = "availability_error:";
        public const string EverythingQueryErrorPrefix = "everything_query_error:";
        public const string EverythingThumbQueryErrorPrefix = "everything_thumb_query_error:";
        public const string EverythingResultTruncatedPrefix = "everything_result_truncated:";
        public const string PathNotEligiblePrefix = "path_not_eligible:";

        public static string BuildAvailabilityError(Exception ex)
        {
            return $"{AvailabilityErrorPrefix}{ex.GetType().Name}";
        }

        public static string BuildEverythingQueryError(Exception ex)
        {
            return $"{EverythingQueryErrorPrefix}{ex.GetType().Name}";
        }

        public static string BuildEverythingThumbQueryError(Exception ex)
        {
            return $"{EverythingThumbQueryErrorPrefix}{ex.GetType().Name}";
        }

        public static string BuildEverythingResultTruncated(long numItems, long totalItems)
        {
            return $"{EverythingResultTruncatedPrefix}{numItems}/{totalItems}";
        }
    }
}
