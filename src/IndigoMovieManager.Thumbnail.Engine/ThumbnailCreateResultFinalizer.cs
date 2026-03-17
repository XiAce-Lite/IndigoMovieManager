using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 生成後の placeholder / marker / cache / process log をまとめて仕上げる。
    /// </summary>
    internal sealed class ThumbnailCreateResultFinalizer
    {
        private readonly IThumbnailCreateProcessLogWriter processLogWriter;
        private readonly ThumbnailMovieMetaResolver movieMetaResolver;

        public ThumbnailCreateResultFinalizer(
            IThumbnailCreateProcessLogWriter processLogWriter,
            ThumbnailMovieMetaResolver movieMetaResolver
        )
        {
            this.processLogWriter =
                processLogWriter ?? throw new ArgumentNullException(nameof(processLogWriter));
            this.movieMetaResolver =
                movieMetaResolver ?? throw new ArgumentNullException(nameof(movieMetaResolver));
        }

        public ThumbnailCreateResult FinalizeImmediate(
            ThumbnailImmediateFinalizationRequest request
        )
        {
            if (request == null)
            {
                return ThumbnailCreationService.CreateFailedResult("", null, "finalizer request is null");
            }

            ThumbnailCreateResult result =
                request.Result
                ?? ThumbnailCreationService.CreateFailedResult(
                    request.OutputPath,
                    request.KnownDurationSec,
                    "finalizer result is null"
                );
            return AttachProcessLog(
                result,
                request.EngineId,
                request.MovieFullPath,
                request.Codec,
                request.KnownDurationSec,
                request.FileSizeBytes
            );
        }

        public ThumbnailCreateResult FinalizeExecution(
            ThumbnailExecutionFinalizationRequest request
        )
        {
            if (request == null)
            {
                return ThumbnailCreationService.CreateFailedResult("", null, "finalizer request is null");
            }

            ThumbnailCreateResult result =
                request.Result
                ?? ThumbnailCreationService.CreateFailedResult(
                    request.Context?.SaveThumbFileName ?? "",
                    request.KnownDurationSec,
                    "thumbnail result is null"
                );
            string processEngineId = request.ProcessEngineId ?? "";
            ThumbnailJobContext context = request.Context;
            if (context == null)
            {
                return AttachProcessLog(
                    result,
                    processEngineId,
                    request.MovieFullPath,
                    "",
                    request.KnownDurationSec,
                    0
                );
            }

            // 全エンジン失敗時は、既知エラーを分類して専用プレースホルダー画像へ置き換える。
            if (!result.IsSuccess && !context.IsManual)
            {
                ThumbnailFailurePlaceholderKind placeholderKind =
                    ThumbnailFailurePlaceholderWriter.ClassifyFailureKind(
                        context.VideoCodec,
                        request.EngineErrorMessages
                    );
                if (
                    ThumbnailFailurePlaceholderWriter.TryCreate(
                        context,
                        placeholderKind,
                        out string placeholderDetail
                    )
                )
                {
                    processEngineId = ThumbnailFailurePlaceholderWriter.ResolveProcessEngineId(
                        placeholderKind
                    );
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"failure placeholder created: kind={placeholderKind}, movie='{request.MovieFullPath}', path='{context.SaveThumbFileName}', detail='{placeholderDetail}'"
                    );
                    result = ThumbnailCreationService.CreateSuccessResult(
                        context.SaveThumbFileName,
                        request.KnownDurationSec
                    );
                }
            }

            // 全エンジン失敗時は #ERROR marker を出して次回誤判定を防ぐ。
            if (!result.IsSuccess && !context.IsManual)
            {
                ThumbnailOutputMarkerCoordinator.ApplyFailureMarker(
                    context.ThumbnailOutPath,
                    request.MovieFullPath,
                    message => ThumbnailRuntimeLog.Write("thumbnail", message)
                );
            }

            if (
                (!request.KnownDurationSec.HasValue || request.KnownDurationSec.Value <= 0)
                && result.DurationSec.HasValue
                && result.DurationSec.Value > 0
                && !string.IsNullOrWhiteSpace(request.CacheKey)
            )
            {
                movieMetaResolver.CacheDuration(
                    request.CacheKey,
                    request.CacheMeta,
                    result.DurationSec
                );
            }

            if (result.IsSuccess)
            {
                // 成功jpgの横に stale な #ERROR が残ると Grid がそちらを拾うため、成功直後に消す。
                ThumbnailOutputMarkerCoordinator.CleanupSuccessMarker(
                    context.ThumbnailOutPath,
                    request.MovieFullPath,
                    message => ThumbnailRuntimeLog.Write("thumbnail", message)
                );
            }

            return AttachProcessLog(
                result,
                processEngineId,
                request.MovieFullPath,
                context.VideoCodec,
                request.KnownDurationSec,
                context.FileSizeBytes
            );
        }

        private ThumbnailCreateResult AttachProcessLog(
            ThumbnailCreateResult result,
            string engineId,
            string movieFullPath,
            string codec,
            double? knownDurationSec,
            long fileSizeBytes
        )
        {
            if (result == null)
            {
                result = ThumbnailCreationService.CreateFailedResult(
                    "",
                    knownDurationSec,
                    "result is null"
                );
            }

            result.ProcessEngineId = engineId ?? "";

            double? loggedDurationSec = result.DurationSec;
            if (
                (!loggedDurationSec.HasValue || loggedDurationSec.Value <= 0)
                && knownDurationSec.HasValue
                && knownDurationSec.Value > 0
            )
            {
                loggedDurationSec = knownDurationSec;
            }

            processLogWriter.Write(
                new ThumbnailCreateProcessLogEntry
                {
                    EngineId = engineId ?? "",
                    MovieFullPath = movieFullPath ?? "",
                    Codec = codec ?? "",
                    DurationSec = loggedDurationSec,
                    FileSizeBytes = fileSizeBytes,
                    OutputPath = result.SaveThumbFileName ?? "",
                    IsSuccess = result.IsSuccess,
                    ErrorMessage = result.ErrorMessage ?? "",
                }
            );
            return result;
        }
    }

    internal sealed class ThumbnailImmediateFinalizationRequest
    {
        public ThumbnailCreateResult Result { get; init; }
        public string EngineId { get; init; } = "";
        public string MovieFullPath { get; init; } = "";
        public string Codec { get; init; } = "";
        public double? KnownDurationSec { get; init; }
        public long FileSizeBytes { get; init; }
        public string OutputPath { get; init; } = "";
    }

    internal sealed class ThumbnailExecutionFinalizationRequest
    {
        public ThumbnailCreateResult Result { get; init; }
        public string ProcessEngineId { get; init; } = "";
        public ThumbnailJobContext Context { get; init; }
        public IReadOnlyList<string> EngineErrorMessages { get; init; } = [];
        public string MovieFullPath { get; init; } = "";
        public double? KnownDurationSec { get; init; }
        public string CacheKey { get; init; } = "";
        public CachedMovieMeta CacheMeta { get; init; }
    }
}
