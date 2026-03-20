namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 生成前の即時返却系 precheck を service 本体から切り離す。
    /// </summary>
    internal sealed class ThumbnailPrecheckCoordinator
    {
        private static readonly string[] KnownMovieExtensionsForUnknownSignature =
        [
            ".avi",
            ".wmv",
            ".mpg",
            ".flv",
            ".asf",
            ".mpeg",
            ".mkv",
            ".swf",
            ".ogm",
            ".ogg",
            ".ogv",
            ".mp4",
            ".mov",
            ".avs",
            ".divx",
            ".3gp",
            ".3g2",
            ".m4v",
            ".webm",
        ];

        private readonly IThumbnailCreationHostRuntime hostRuntime;
        private readonly ThumbnailMovieMetaResolver movieMetaResolver;
        private readonly ThumbnailJobContextBuilder jobContextBuilder;
        private readonly ThumbnailCreateResultFinalizer resultFinalizer;

        public ThumbnailPrecheckCoordinator(
            IThumbnailCreationHostRuntime hostRuntime,
            ThumbnailMovieMetaResolver movieMetaResolver,
            ThumbnailJobContextBuilder jobContextBuilder,
            ThumbnailCreateResultFinalizer resultFinalizer
        )
        {
            this.hostRuntime = hostRuntime ?? throw new ArgumentNullException(nameof(hostRuntime));
            this.movieMetaResolver =
                movieMetaResolver ?? throw new ArgumentNullException(nameof(movieMetaResolver));
            this.jobContextBuilder =
                jobContextBuilder ?? throw new ArgumentNullException(nameof(jobContextBuilder));
            this.resultFinalizer =
                resultFinalizer ?? throw new ArgumentNullException(nameof(resultFinalizer));
        }

        public ThumbnailPrecheckOutcome Run(ThumbnailPrecheckRequest request)
        {
            if (request == null)
            {
                return ThumbnailPrecheckOutcome.Immediate(
                    ThumbnailCreateResultFactory.CreateFailed(
                        "",
                        null,
                        "precheck request is null"
                    )
                );
            }

            if (request.IsManual && !Path.Exists(request.SaveThumbFileName))
            {
                ThumbnailMovieTraceLog.Write(
                    request.TraceId,
                    source: "engine",
                    phase: "precheck_manual_target_missing",
                    moviePath: request.MovieFullPath,
                    sourceMoviePath: request.SourceMovieFullPath,
                    tabIndex: request.Request?.TabIndex ?? -1,
                    result: "failed",
                    detail: "manual target thumbnail does not exist",
                    outputPath: request.SaveThumbFileName
                );
                // 手動更新は既存サムネイルが前提。
                return ThumbnailPrecheckOutcome.Immediate(
                    resultFinalizer.FinalizeImmediate(
                        new ThumbnailImmediateFinalizationRequest
                        {
                            Result = ThumbnailCreateResultFactory.CreateFailed(
                                request.SaveThumbFileName,
                                request.KnownDurationSec,
                                "manual target thumbnail does not exist"
                            ),
                            EngineId = "precheck",
                            MovieFullPath = request.MovieFullPath,
                            KnownDurationSec = request.KnownDurationSec,
                            OutputPath = request.SaveThumbFileName,
                            TraceId = request.TraceId,
                        }
                    )
                );
            }

            if (!Path.Exists(request.ThumbnailOutPath))
            {
                Directory.CreateDirectory(request.ThumbnailOutPath);
            }

            if (!Path.Exists(request.SourceMovieFullPath))
            {
                ThumbnailMovieTraceLog.Write(
                    request.TraceId,
                    source: "engine",
                    phase: "precheck_missing_movie",
                    moviePath: request.MovieFullPath,
                    sourceMoviePath: request.SourceMovieFullPath,
                    tabIndex: request.Request?.TabIndex ?? -1,
                    result: "success",
                    detail: "movie file not found, placeholder copied",
                    outputPath: request.SaveThumbFileName
                );
                if (!Path.Exists(request.SaveThumbFileName))
                {
                    string noFileJpeg = hostRuntime.ResolveMissingMoviePlaceholderPath(
                        request.Request?.TabIndex ?? 0
                    );
                    File.Copy(noFileJpeg, request.SaveThumbFileName, true);
                }

                return ThumbnailPrecheckOutcome.Immediate(
                    resultFinalizer.FinalizeImmediate(
                        new ThumbnailImmediateFinalizationRequest
                        {
                            Result = ThumbnailCreateResultFactory.CreateSuccess(
                                request.SaveThumbFileName,
                                request.KnownDurationSec
                            ),
                            EngineId = "missing-movie",
                            MovieFullPath = request.MovieFullPath,
                            KnownDurationSec = request.KnownDurationSec,
                            OutputPath = request.SaveThumbFileName,
                            TraceId = request.TraceId,
                        }
                    )
                );
            }

            long fileSizeBytes = movieMetaResolver.ResolveFileSizeBytes(
                request.SourceMovieFullPath,
                request.Request?.MovieSizeBytes ?? 0
            );
            if (fileSizeBytes > 0 && request.Request != null)
            {
                // 後段で同じ情報を再利用できるよう、取得できたサイズを入力契約へ戻しておく。
                request.Request.MovieSizeBytes = fileSizeBytes;
            }

            if (!request.IsManual)
            {
                ThumbnailFailurePlaceholderKind knownPlaceholderKind = ResolveImmediatePlaceholderKind(
                    request.SourceMovieFullPath,
                    fileSizeBytes
                );
                if (knownPlaceholderKind != ThumbnailFailurePlaceholderKind.None)
                {
                    return CreateImmediatePlaceholderOutcome(
                        request,
                        fileSizeBytes,
                        knownPlaceholderKind
                    );
                }
            }

            if (!request.IsManual && request.CacheMeta?.IsDrmSuspected == true)
            {
                // DRM判定ヒット時はデコーダーへ進まず、即プレースホルダーを生成して完了扱いにする。
                string drmDetail = string.IsNullOrWhiteSpace(request.CacheMeta.DrmDetail)
                    ? "drm_suspected"
                    : request.CacheMeta.DrmDetail;
                ThumbnailJobContextBuildOutcome drmContextOutcome = jobContextBuilder.Build(
                    new ThumbnailJobContextBuildRequest
                    {
                        Request = request.Request,
                        LayoutProfile = request.LayoutProfile,
                        ThumbnailOutPath = request.ThumbnailOutPath,
                        MovieFullPath = request.MovieFullPath,
                        SourceMovieFullPath = request.SourceMovieFullPath,
                        SaveThumbFileName = request.SaveThumbFileName,
                        IsResizeThumb = request.IsResizeThumb,
                        IsManual = request.IsManual,
                        DurationSec = request.KnownDurationSec,
                        FileSizeBytes = fileSizeBytes,
                        TraceId = request.TraceId,
                    }
                );
                if (!drmContextOutcome.IsSuccess)
                {
                    return ThumbnailPrecheckOutcome.Immediate(
                        resultFinalizer.FinalizeImmediate(
                            new ThumbnailImmediateFinalizationRequest
                            {
                                Result = ThumbnailCreateResultFactory.CreateFailed(
                                    request.SaveThumbFileName,
                                    request.KnownDurationSec,
                                    drmContextOutcome.ErrorMessage
                                ),
                                EngineId = "precheck",
                                MovieFullPath = request.MovieFullPath,
                                KnownDurationSec = request.KnownDurationSec,
                                OutputPath = request.SaveThumbFileName,
                                TraceId = request.TraceId,
                            }
                        )
                    );
                }

                if (
                    ThumbnailFailurePlaceholderWriter.TryCreate(
                        drmContextOutcome.Context,
                        ThumbnailFailurePlaceholderKind.DrmSuspected,
                        out string placeholderDetail
                    )
                )
                {
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"drm precheck hit: movie='{request.MovieFullPath}', detail='{drmDetail}', placeholder='{placeholderDetail}'"
                    );
                    return ThumbnailPrecheckOutcome.Immediate(
                        resultFinalizer.FinalizeImmediate(
                            new ThumbnailImmediateFinalizationRequest
                            {
                                Result = ThumbnailCreateResultFactory.CreateSuccess(
                                    request.SaveThumbFileName,
                                    request.KnownDurationSec
                                ),
                                EngineId = "placeholder-drm-precheck",
                                MovieFullPath = request.MovieFullPath,
                                KnownDurationSec = request.KnownDurationSec,
                                FileSizeBytes = fileSizeBytes,
                                OutputPath = request.SaveThumbFileName,
                                TraceId = request.TraceId,
                            }
                        )
                    );
                }

                string error = $"drm precheck hit but placeholder failed: {drmDetail}";
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"drm precheck failed: movie='{request.MovieFullPath}', reason='{error}'"
                );
                return ThumbnailPrecheckOutcome.Immediate(
                    resultFinalizer.FinalizeImmediate(
                        new ThumbnailImmediateFinalizationRequest
                        {
                            Result = ThumbnailCreateResultFactory.CreateFailed(
                                request.SaveThumbFileName,
                                request.KnownDurationSec,
                                error
                            ),
                            EngineId = "drm-precheck",
                            MovieFullPath = request.MovieFullPath,
                            KnownDurationSec = request.KnownDurationSec,
                            FileSizeBytes = fileSizeBytes,
                            OutputPath = request.SaveThumbFileName,
                            TraceId = request.TraceId,
                        }
                    )
                );
            }

            return ThumbnailPrecheckOutcome.Continue(fileSizeBytes);
        }

        private ThumbnailPrecheckOutcome CreateImmediatePlaceholderOutcome(
            ThumbnailPrecheckRequest request,
            long fileSizeBytes,
            ThumbnailFailurePlaceholderKind kind
        )
        {
            ThumbnailJobContextBuildOutcome contextOutcome = jobContextBuilder.Build(
                new ThumbnailJobContextBuildRequest
                {
                    Request = request.Request,
                    LayoutProfile = request.LayoutProfile,
                    ThumbnailOutPath = request.ThumbnailOutPath,
                    MovieFullPath = request.MovieFullPath,
                    SourceMovieFullPath = request.SourceMovieFullPath,
                    SaveThumbFileName = request.SaveThumbFileName,
                    IsResizeThumb = request.IsResizeThumb,
                    IsManual = request.IsManual,
                    DurationSec = request.KnownDurationSec,
                    FileSizeBytes = fileSizeBytes,
                    TraceId = request.TraceId,
                }
            );
            if (!contextOutcome.IsSuccess)
            {
                return ThumbnailPrecheckOutcome.Immediate(
                    resultFinalizer.FinalizeImmediate(
                        new ThumbnailImmediateFinalizationRequest
                        {
                            Result = ThumbnailCreateResultFactory.CreateFailed(
                                request.SaveThumbFileName,
                                request.KnownDurationSec,
                                contextOutcome.ErrorMessage
                            ),
                            EngineId = "precheck",
                            MovieFullPath = request.MovieFullPath,
                            KnownDurationSec = request.KnownDurationSec,
                            FileSizeBytes = fileSizeBytes,
                            OutputPath = request.SaveThumbFileName,
                            TraceId = request.TraceId,
                        }
                    )
                );
            }

            if (
                ThumbnailFailurePlaceholderWriter.TryCreate(
                    contextOutcome.Context,
                    kind,
                    out string placeholderDetail
                )
            )
            {
                string processEngineId = ThumbnailFailurePlaceholderWriter.ResolveProcessEngineId(kind);
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"precheck placeholder hit: kind={kind}, movie='{request.MovieFullPath}', detail='{placeholderDetail}'"
                );
                return ThumbnailPrecheckOutcome.Immediate(
                    resultFinalizer.FinalizeImmediate(
                        new ThumbnailImmediateFinalizationRequest
                        {
                            Result = ThumbnailCreateResultFactory.CreateSuccess(
                                request.SaveThumbFileName,
                                request.KnownDurationSec
                            ),
                            EngineId = processEngineId,
                            MovieFullPath = request.MovieFullPath,
                            KnownDurationSec = request.KnownDurationSec,
                            FileSizeBytes = fileSizeBytes,
                            OutputPath = request.SaveThumbFileName,
                            TraceId = request.TraceId,
                        }
                    )
                );
            }

            string error = $"precheck placeholder failed: {kind}";
            ThumbnailRuntimeLog.Write(
                "thumbnail",
                $"precheck placeholder failed: kind={kind}, movie='{request.MovieFullPath}', reason='{error}'"
            );
            return ThumbnailPrecheckOutcome.Immediate(
                resultFinalizer.FinalizeImmediate(
                    new ThumbnailImmediateFinalizationRequest
                    {
                        Result = ThumbnailCreateResultFactory.CreateFailed(
                            request.SaveThumbFileName,
                            request.KnownDurationSec,
                            error
                        ),
                        EngineId = "precheck",
                        MovieFullPath = request.MovieFullPath,
                        KnownDurationSec = request.KnownDurationSec,
                        FileSizeBytes = fileSizeBytes,
                        OutputPath = request.SaveThumbFileName,
                        TraceId = request.TraceId,
                    }
                )
            );
        }

        private static ThumbnailFailurePlaceholderKind ResolveImmediatePlaceholderKind(
            string movieFullPath,
            long fileSizeBytes
        )
        {
            if (fileSizeBytes <= 0 && Path.Exists(movieFullPath))
            {
                return ThumbnailFailurePlaceholderKind.NoData;
            }

            ThumbnailFileSignatureKind signatureKind = ThumbnailFileSignatureInspector.Inspect(
                movieFullPath
            );
            if (signatureKind == ThumbnailFileSignatureKind.AppleDouble)
            {
                return ThumbnailFailurePlaceholderKind.AppleDouble;
            }

            if (signatureKind == ThumbnailFileSignatureKind.ShockwaveFlash)
            {
                return ThumbnailFailurePlaceholderKind.ShockwaveFlash;
            }

            if (signatureKind == ThumbnailFileSignatureKind.Unknown)
            {
                // 拡張子が既知動画なら、先頭64バイトだけで非動画確定しない。
                return HasKnownMovieLikeExtension(movieFullPath)
                    ? ThumbnailFailurePlaceholderKind.None
                    : ThumbnailFailurePlaceholderKind.NotMovie;
            }

            return ThumbnailFailurePlaceholderKind.None;
        }

        private static bool HasKnownMovieLikeExtension(string movieFullPath)
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            string ext = Path.GetExtension(movieFullPath);
            if (string.IsNullOrWhiteSpace(ext))
            {
                return false;
            }

            for (int i = 0; i < KnownMovieExtensionsForUnknownSignature.Length; i++)
            {
                if (
                    ext.Equals(
                        KnownMovieExtensionsForUnknownSignature[i],
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal sealed class ThumbnailPrecheckRequest
    {
        public ThumbnailRequest Request { get; init; } = new();
        public ThumbnailLayoutProfile LayoutProfile { get; init; }
        public string ThumbnailOutPath { get; init; } = "";
        public string MovieFullPath { get; init; } = "";
        public string SourceMovieFullPath { get; init; } = "";
        public string SaveThumbFileName { get; init; } = "";
        public bool IsResizeThumb { get; init; }
        public bool IsManual { get; init; }
        public double? KnownDurationSec { get; init; }
        public CachedMovieMeta CacheMeta { get; init; }
        public string TraceId { get; init; } = "";
    }

    internal sealed class ThumbnailPrecheckOutcome
    {
        private ThumbnailPrecheckOutcome(ThumbnailCreateResult immediateResult, long fileSizeBytes)
        {
            ImmediateResult = immediateResult;
            FileSizeBytes = fileSizeBytes;
        }

        public ThumbnailCreateResult ImmediateResult { get; }
        public long FileSizeBytes { get; }
        public bool HasImmediateResult => ImmediateResult != null;

        public static ThumbnailPrecheckOutcome Immediate(ThumbnailCreateResult result)
        {
            return new ThumbnailPrecheckOutcome(result, 0);
        }

        public static ThumbnailPrecheckOutcome Continue(long fileSizeBytes)
        {
            return new ThumbnailPrecheckOutcome(null, fileSizeBytes);
        }
    }
}
