using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using IndigoMovieManager.Thumbnail.Engines;
using static IndigoMovieManager.Thumbnail.Tools;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 【サムネイル生成の頭脳（コアロジック）】
    /// 動画ファイルから画像フレームを抽出し、リサイズ・結合して1枚のサムネイル画像を作成するサービスクラスです。
    ///
    /// ＜主な処理の流れ (CreateThumbAsync)＞
    /// 1. 事前準備: キャッシュ確認や重複実行防止のロック取得、出力先フォルダの準備を行います。
    /// 2. 入力パスの決定: OpenCVが絵文字などの特殊パスに弱いため、短いパスやジャンクション（別名）など「開けるパス」を探索します (SelectOpenCvInputPath)。
    /// 3. フレーム抽出: OpenCVで動画を開き、指定された分割数に応じた秒数位置にシークして画像（Mat）を取得・リサイズします。
    /// 4. フォールバック: OpenCVでどうしても開けない場合は、別のツール(ffmpeg)を使って画像を抽出します (TryCreateThumbByFfmpegAsync)。
    /// 5. 画像合成と保存: 抽出した複数枚の画像を1枚のキャンバスにタイル状に並べて結合し、JPEGとして保存します (SaveCombinedThumbnail)。
    /// 【サムネイル生成のオーケストレータ】
    /// ルーティング規則に従ってエンジンを選び、生成を実行する。
    /// </summary>
    public sealed class ThumbnailCreationService
    {
        // .NET では既定で一部コードページ（例: 932）が無効なため、
        // 既存処理互換としてCodePagesプロバイダを有効化しておく。
        static ThumbnailCreationService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private readonly IThumbnailGenerationEngine ffMediaToolkitEngine;
        private readonly IThumbnailGenerationEngine ffmpegOnePassEngine;
        private readonly IThumbnailGenerationEngine openCvEngine;
        private readonly IThumbnailGenerationEngine autogenEngine;
        private readonly ThumbnailEngineRouter engineRouter;

        // 同一出力ファイルへの同時書き込みを防ぐ。
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> OutputFileLocks = new(
            StringComparer.OrdinalIgnoreCase
        );

        // 同一動画の再処理を軽くするため、ハッシュと動画秒数をキャッシュする。
        private static readonly ConcurrentDictionary<string, CachedMovieMeta> MovieMetaCache = new(
            StringComparer.OrdinalIgnoreCase
        );
        private const int MovieMetaCacheMaxCount = 10000;
        private static readonly object ThumbnailProcessLogLock = new();
        private const string ThumbnailProcessLogFileName = "thumbnail-create-process.csv";
        private const string EngineEnvName = "IMM_THUMB_ENGINE";

        public ThumbnailCreationService()
            : this(
                new FfMediaToolkitThumbnailGenerationEngine(),
                new FfmpegOnePassThumbnailGenerationEngine(),
                new OpenCvThumbnailGenerationEngine(),
                new FfmpegAutoGenThumbnailGenerationEngine()
            ) { }

        internal ThumbnailCreationService(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine
        )
        {
            this.ffMediaToolkitEngine =
                ffMediaToolkitEngine
                ?? throw new ArgumentNullException(nameof(ffMediaToolkitEngine));
            this.ffmpegOnePassEngine =
                ffmpegOnePassEngine ?? throw new ArgumentNullException(nameof(ffmpegOnePassEngine));
            this.openCvEngine =
                openCvEngine ?? throw new ArgumentNullException(nameof(openCvEngine));
            this.autogenEngine =
                autogenEngine ?? throw new ArgumentNullException(nameof(autogenEngine));

            engineRouter = new ThumbnailEngineRouter([
                this.ffMediaToolkitEngine,
                this.ffmpegOnePassEngine,
                this.openCvEngine,
                this.autogenEngine,
            ]);
        }

        // ブックマーク用の単一フレームサムネイルを生成する。
        public async Task<bool> CreateBookmarkThumbAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos
        )
        {
            if (!Path.Exists(movieFullPath))
            {
                return false;
            }

            IThumbnailGenerationEngine engine = engineRouter.ResolveForBookmark();
                try
                {
                return await engine.CreateBookmarkAsync(
                        movieFullPath,
                    saveThumbPath,
                    capturePos,
                    CancellationToken.None
                    );
                    if (string.IsNullOrWhiteSpace(moviePathForOpenCv))
                    {
                        return;
                    }
            catch (Exception ex)
                    {
                DebugRuntimeLog.Write(
                    "thumbnail",
                    $"bookmark create failed: engine={engine.EngineId}, movie='{movieFullPath}', err='{ex.Message}'"
                );
                return false;
                    }
                    capture.Grab();

                    using var img = new Mat();
                    capture.PosMsec = capturePos * 1000;
                    int msecCounter = 0;
                    while (!capture.Read(img))
                    {
                        capture.PosMsec += 100;
                        if (msecCounter > 100)
                        {
                            break;
                        }
                        msecCounter++;
                    }

                    if (img.Empty())
                    {
                        return;
                    }

                    using Mat temp = new(img, GetAspect(img.Width, img.Height));
                    using Mat dst = new();
                    OpenCvSharp.Size sz = new(640, 480);
                    Cv2.Resize(temp, dst, sz);
                    OpenCvSharp
                        .Extensions.BitmapConverter.ToBitmap(dst)
                        .Save(saveThumbPath, ImageFormat.Jpeg);
                    created = true;
                }
                finally
                {
                    CleanupTempDirectory(tempRootDir);
                }
            });

            return created;
        }

        /// <summary>
        /// 通常・手動のサムネイル生成を行うメイン・エントリーポイント。
        /// </summary>
        public async Task<ThumbnailCreateResult> CreateThumbAsync(
            QueueObj queueObj,
            string dbName,
            string thumbFolder,
            bool isResizeThumb,
            bool isManual = false,
            CancellationToken cts = default
        )
        {
            TabInfo tbi = new(queueObj.Tabindex, dbName, thumbFolder);
            string movieFullPath = queueObj.MovieFullPath;
            string moviePathForOpenCv = movieFullPath;

            var cacheMeta = GetCachedMovieMeta(movieFullPath, out string cacheKey);
            string hash = cacheMeta.Hash;
            double? durationSec = cacheMeta.DurationSec;

            string saveThumbFileName = ThumbnailPathResolver.BuildThumbnailPath(
                tbi,
                movieFullPath,
                hash
            );
            var outputLock = OutputFileLocks.GetOrAdd(
                saveThumbFileName,
                _ => new SemaphoreSlim(1, 1)
            );
            await outputLock.WaitAsync(cts);

            try
            {
                // 返却直前に処理ログを確実に残すため、戻り値生成をこの関数に集約する。
                ThumbnailCreateResult ReturnWithProcessLog(
                    ThumbnailCreateResult result,
                    string engineId,
                    string codec,
                    long fileSizeBytes
                )
                {
                    double? loggedDurationSec = result.DurationSec;
                    if (
                        (!loggedDurationSec.HasValue || loggedDurationSec.Value <= 0)
                        && durationSec.HasValue
                        && durationSec.Value > 0
                    )
                    {
                        loggedDurationSec = durationSec;
                    }

                    WriteThumbnailCreateProcessLog(
                        engineId,
                        movieFullPath,
                        codec,
                        loggedDurationSec,
                        fileSizeBytes,
                        result.SaveThumbFileName,
                        result.IsSuccess,
                        result.ErrorMessage
                    );
                    return result;
                }

                if (isManual && !Path.Exists(saveThumbFileName))
                {
                    // 手動更新は既存サムネイルが前提。
                    return ReturnWithProcessLog(
                        CreateFailedResult(
                            saveThumbFileName,
                            durationSec,
                            "manual target thumbnail does not exist"
                        ),
                        "precheck",
                        "",
                        0
                    );
                }

                if (!Path.Exists(tbi.OutPath))
                {
                    Directory.CreateDirectory(tbi.OutPath);
                }

                if (!Path.Exists(movieFullPath))
                {
                    if (!Path.Exists(saveThumbFileName))
                    {
                        string noFileJpeg = Path.Combine(Directory.GetCurrentDirectory(), "Images");
                        noFileJpeg = queueObj.Tabindex switch
                        {
                            0 => Path.Combine(noFileJpeg, "noFileSmall.jpg"),
                            1 => Path.Combine(noFileJpeg, "noFileBig.jpg"),
                            2 => Path.Combine(noFileJpeg, "noFileGrid.jpg"),
                            3 => Path.Combine(noFileJpeg, "noFileList.jpg"),
                            4 => Path.Combine(noFileJpeg, "noFileBig.jpg"),
                            99 => Path.Combine(noFileJpeg, "noFileGrid.jpg"),
                            _ => Path.Combine(noFileJpeg, "noFileSmall.jpg"),
                        };
                        File.Copy(noFileJpeg, saveThumbFileName, true);
                    }

                    return ReturnWithProcessLog(
                        CreateSuccessResult(saveThumbFileName, durationSec),
                        "missing-movie",
                        "",
                        0
                        );
                        if (string.IsNullOrWhiteSpace(moviePathForOpenCv))
                        {
                            return await CreateFallbackResultAsync().ConfigureAwait(false);
                        }

                        ConfigureGpuDecodeOptionsFromEnv();
                        using var capture = new VideoCapture(moviePathForOpenCv);
                        if (!capture.IsOpened())
                        {
                            LogVideoCaptureOpenFailed(
                                "QueueThumb",
                                movieFullPath,
                                moviePathForOpenCv
                            );
                            return await CreateFallbackResultAsync().ConfigureAwait(false);
                        }
                        capture.Grab();

                        // 【STEP 2: 動画の長さ（秒数）を確定させる】
                        // まず軽い手段で長さを算出し、無効時だけShellへフォールバックする。
                        if (!durationSec.HasValue || durationSec.Value <= 0)
                        {
                            double frameCount = capture.Get(VideoCaptureProperties.FrameCount);
                            double fps = capture.Get(VideoCaptureProperties.Fps);
                            durationSec = TryGetDurationSec(frameCount, fps);
                            if (!durationSec.HasValue || durationSec.Value <= 0)
                            {
                                durationSec = TryGetDurationSecFromShell(movieFullPath);
                            }
                            CacheMovieDuration(cacheKey, hash, durationSec);
                        }

                ThumbInfo thumbInfo;
                        if (isManual)
                        {
                    thumbInfo = new ThumbInfo();
                            thumbInfo.GetThumbInfo(saveThumbFileName);
                            if (!thumbInfo.IsThumbnail)
                            {
                        return ReturnWithProcessLog(
                            CreateFailedResult(
                                saveThumbFileName,
                                durationSec,
                                "manual source thumbnail metadata is missing"
                            ),
                            "precheck",
                            "",
                            0
                        );
                            }

                            if ((queueObj.ThumbPanelPos != null) && (queueObj.ThumbTimePos != null))
                            {
                        int panelPos = (int)queueObj.ThumbPanelPos;
                        if (panelPos >= 0 && panelPos < thumbInfo.ThumbSec.Count)
                        {
                            thumbInfo.ThumbSec[panelPos] = (int)queueObj.ThumbTimePos;
                            }
                        }
                    thumbInfo.NewThumbInfo();
                }
                        else
                        {
                    thumbInfo = BuildAutoThumbInfo(tbi, durationSec);
                            }
                        }
                        thumbInfo.NewThumbInfo();

                long fileSizeBytes = 0;
                        try
                        {
                    fileSizeBytes = new FileInfo(movieFullPath).Length;
                                    }
                catch
                                {
                    fileSizeBytes = 0;
                                }

                double? avgBitrateMbps = null;
                if (fileSizeBytes > 0 && durationSec.HasValue && durationSec.Value > 0)
                                {
                    avgBitrateMbps = (fileSizeBytes * 8d) / (durationSec.Value * 1_000_000d);
                                }
                                else if (
                                    !targetSize.HasValue
                                    || targetSize.Value.Width == 0
                                    || targetSize.Value.Height == 0
                                )
                                {
                                    targetSize = new OpenCvSharp.Size
                                    {
                                        Width = cropped.Width < 320 ? cropped.Width : 320,
                                        Height = cropped.Height < 240 ? cropped.Height : 240,
                                    };
                                }

                ThumbnailJobContext context = new()
                            {
                    QueueObj = queueObj,
                    TabInfo = tbi,
                    ThumbInfo = thumbInfo,
                    MovieFullPath = movieFullPath,
                                    SaveThumbFileName = saveThumbFileName,
                    IsResizeThumb = isResizeThumb,
                    IsManual = isManual,
                                    DurationSec = durationSec,
                    FileSizeBytes = fileSizeBytes,
                    AverageBitrateMbps = avgBitrateMbps,
                    HasEmojiPath = ThumbnailEngineRouter.HasUnmappableAnsiChar(movieFullPath),
                    VideoCodec = new MovieInfo(movieFullPath, noHash: true).VideoCodec ?? "",
                                };
                            }

                IThumbnailGenerationEngine selectedEngine = engineRouter.ResolveForThumbnail(
                    context
                            );
                List<IThumbnailGenerationEngine> engineOrder = BuildThumbnailEngineOrder(
                    selectedEngine,
                    context
                                );
                ThumbnailCreateResult result = null;
                IThumbnailGenerationEngine executedEngine = selectedEngine;

                for (int i = 0; i < engineOrder.Count; i++)
                        {
                    IThumbnailGenerationEngine candidate = engineOrder[i];
                    executedEngine = candidate;
                    DebugRuntimeLog.Write(
                        "thumbnail",
                        i == 0
                            ? $"engine selected: id={candidate.EngineId}, panel={context.PanelCount}, size={context.FileSizeBytes}, avg_mbps={context.AverageBitrateMbps:0.###}, emoji={context.HasEmojiPath}, manual={context.IsManual}"
                            : $"engine fallback: from={selectedEngine.EngineId}, to={candidate.EngineId}, attempt={i + 1}/{engineOrder.Count}"
                    );
                }

                    result = await candidate.CreateAsync(context, cts);
                    if (result.IsSuccess)
                {
                        break;
            }
            finally
            {
                outputLock.Release();
            }
        }

                    if (i < engineOrder.Count - 1)
        {
                        DebugRuntimeLog.Write(
                            "thumbnail",
                            $"engine failed: id={candidate.EngineId}, reason='{result.ErrorMessage}', try_next=True"
            );

            for (int i = 0; i < total; i++)
            {
                int r = i / columns;
                int c = i % columns;
                var rect = new OpenCvSharp.Rect(
                    c * frameWidth,
                    r * frameHeight,
                    frameWidth,
                    frameHeight
                );
                using Mat roi = new(canvas, rect);
                frames[i].CopyTo(roi);
            }

            if (Path.Exists(saveThumbFileName))
            {
                File.Delete(saveThumbFileName);
            }

                if (result == null)
            {
                    result = CreateFailedResult(
                        saveThumbFileName,
                        durationSec,
                        "thumbnail engine was not executed"
                    );
                    requireFallback = true;
                }
                if (
                    (!durationSec.HasValue || durationSec.Value <= 0)
                    && result.DurationSec.HasValue
                    && result.DurationSec.Value > 0
                )
                {
                    CacheMovieDuration(cacheKey, hash, result.DurationSec);
                }
                return ReturnWithProcessLog(
                    result,
                    executedEngine.EngineId,
                    context.VideoCodec,
                    context.FileSizeBytes
            );
            Directory.CreateDirectory(tempDir);

            string tempSavePath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.jpg");
            try
            {
                // OpenCV へは ASCII 安全な一時パスのみを渡す。
                bool tempSaved = Cv2.ImWrite(tempSavePath, canvas);
                if (!tempSaved)
                {
                    return false;
                }

                // 書き込み完了後に、.NET のファイル移動で最終パスへ配置する。
                // File.Move は Unicode パスを扱えるため、絵文字パスでも移動可能。
                File.Move(tempSavePath, saveThumbFileName, true);
                return true;
            }
            finally
            {
                outputLock.Release();
                }
            }
        }

        internal static ThumbnailCreateResult CreateSuccessResult(
            string saveThumbFileName,
            double? durationSec
        )
        {
            return new ThumbnailCreateResult
            {
                SaveThumbFileName = saveThumbFileName,
                DurationSec = durationSec,
                IsSuccess = true,
            };

            for (int i = 1; i < thumbInfo.ThumbCounts + 1; i++)
            {
                thumbInfo.Add(i * divideSec);
            }
            thumbInfo.NewThumbInfo();
            return thumbInfo;
        }

        internal static ThumbnailCreateResult CreateFailedResult(
            string saveThumbFileName,
            double? durationSec,
            string errorMessage
        )
        {
            return new ThumbnailCreateResult
            {
                SaveThumbFileName = saveThumbFileName,
                DurationSec = durationSec,
                IsSuccess = false,
                ErrorMessage = errorMessage ?? "",
            };
            }

        // 自動生成時のみ、失敗したら次候補へ送ってサムネイル欠損を減らす。
        private List<IThumbnailGenerationEngine> BuildThumbnailEngineOrder(
            IThumbnailGenerationEngine selectedEngine,
            ThumbnailJobContext context
        )
        {
            List<IThumbnailGenerationEngine> order = [];
            AddEngine(order, selectedEngine);

            bool forced = IsForcedEngineMode();
            if (forced)
            {
                return order;
                }
            }
            candidates.Add(new LibraryInputCandidate(inputPath, stage));
        }

            if (context?.IsManual == true)
            {
                if (
                    string.Equals(
                        selectedEngine?.EngineId,
                        "ffmediatoolkit",
                        StringComparison.OrdinalIgnoreCase
        )
                )
        {
                    AddEngine(order, openCvEngine);
                }
                else
            {
                    AddEngine(order, ffMediaToolkitEngine);
                }
                return order;
            }

            if (
                string.Equals(
                    selectedEngine?.EngineId,
                    "autogen",
                    StringComparison.OrdinalIgnoreCase
        )
            )
        {
                AddEngine(order, ffMediaToolkitEngine);
                AddEngine(order, ffmpegOnePassEngine);
                AddEngine(order, openCvEngine);
                return order;
            }

            if (
                string.Equals(
                    selectedEngine?.EngineId,
                    "ffmediatoolkit",
                    StringComparison.OrdinalIgnoreCase
                        )
                        .ConfigureAwait(false);
                    if (savedByCandidate)
                    {
                        Debug.WriteLine(
                            $"thumb ffmpeg fallback succeeded: stage={candidate.Stage}, path='{candidate.InputPath}'"
                        );
                        return FfmpegFallbackResult.Succeeded();
                    }
                }

                // Raw/Shortで失敗したときだけ別名を作って試す。
                List<LibraryInputCandidate> aliasCandidates = BuildAliasInputCandidates(
                    movieFullPath,
                    tempRootDir
                );
                for (int i = 0; i < aliasCandidates.Count; i++)
                {
                    cts.ThrowIfCancellationRequested();
                    LibraryInputCandidate candidate = aliasCandidates[i];
                    string attemptDir = Path.Combine(tempRootDir, $"attempt-{attemptIndex:D2}");
                    attemptIndex++;
                    bool savedByCandidate = await TryCreateThumbByFfmpegCoreAsync(
                            ffmpegExePath,
                            candidate.InputPath,
                            attemptDir,
                            saveThumbFileName,
                            thumbInfo,
                            columns,
                            rows,
                            targetWidth,
                            targetHeight,
                            cts
                        )
                        .ConfigureAwait(false);
                    if (savedByCandidate)
                    {
                AddEngine(order, autogenEngine);
                AddEngine(order, ffmpegOnePassEngine);
                AddEngine(order, openCvEngine);
                return order;
                }

                // 1-3で通らない場合のみ、最後の手段としてコピーを検討する。
                FfmpegInputPreparationResult copiedInput = PrepareCopiedInputPathForFallback(
                    movieFullPath,
                    tempRootDir
                );
                if (copiedInput.DeferredByLargeCopy)
                {
                    return FfmpegFallbackResult.Deferred(copiedInput.DeferredCopySizeBytes);
                }
                if (
                string.Equals(
                    selectedEngine?.EngineId,
                    "ffmpeg1pass",
                    StringComparison.OrdinalIgnoreCase
                )
                {
                    return FfmpegFallbackResult.Failed();
                }

                bool savedByCopy = await TryCreateThumbByFfmpegCoreAsync(
                        ffmpegExePath,
                        copiedInput.InputPath,
                        Path.Combine(tempRootDir, "attempt-copy"),
                        saveThumbFileName,
                        thumbInfo,
                        columns,
                        rows,
                        targetWidth,
                        targetHeight,
                        cts
                    )
                    .ConfigureAwait(false);

                return savedByCopy
                    ? FfmpegFallbackResult.Succeeded()
                    : FfmpegFallbackResult.Failed();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"thumb ffmpeg fallback exception: {ex.Message}");
                return FfmpegFallbackResult.Failed();
            }
            finally
            {
                AddEngine(order, autogenEngine);
                AddEngine(order, ffMediaToolkitEngine);
                AddEngine(order, openCvEngine);
                return order;
        }

            if (
                string.Equals(
                    selectedEngine?.EngineId,
                    "opencv",
                    StringComparison.OrdinalIgnoreCase
        )
        {
            if (string.IsNullOrWhiteSpace(ffmpegInputPath) || !Path.Exists(ffmpegInputPath))
            {
                return false;
            }

            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, true);
            }
            Directory.CreateDirectory(workDir);

            List<Mat> resizedFrames = [];
            try
            {
                for (int i = 0; i < thumbInfo.ThumbSec.Count; i++)
                {
                    cts.ThrowIfCancellationRequested();
                    string framePath = Path.Combine(workDir, $"{i:D4}.jpg");
                    bool extracted = await TryExtractSingleFrameByFfmpegAsync(
                            ffmpegExePath,
                            ffmpegInputPath,
                            thumbInfo.ThumbSec[i],
                            targetWidth,
                            targetHeight,
                            framePath,
                            cts
                        )
                        .ConfigureAwait(false);
                    if (!extracted)
                    {
                        return false;
                    }

                    // Cloneを避け、読み込んだMatをそのまま保持して最後にまとめてDisposeする。
                    Mat frame = Cv2.ImRead(framePath, ImreadModes.Color);
                    if (frame.Empty())
                    {
                AddEngine(order, autogenEngine);
                AddEngine(order, ffMediaToolkitEngine);
                AddEngine(order, ffmpegOnePassEngine);
                return order;
                }

            AddEngine(order, autogenEngine);
            AddEngine(order, ffMediaToolkitEngine);
            AddEngine(order, ffmpegOnePassEngine);
            AddEngine(order, openCvEngine);
            return order;
                }

        private static bool IsForcedEngineMode()
                {
            string mode = Environment.GetEnvironmentVariable(EngineEnvName)?.Trim() ?? "";
            return !string.IsNullOrWhiteSpace(mode)
                && !string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddEngine(
            List<IThumbnailGenerationEngine> order,
            IThumbnailGenerationEngine engine
        )
        {
            if (engine == null)
            {
                return;
            }

            for (int i = 0; i < order.Count; i++)
            {
                long fileSize = new FileInfo(movieFullPath).Length;
                if (
                    string.Equals(
                        order[i].EngineId,
                        engine.EngineId,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return;
            }
        }

            order.Add(engine);
        }

        // 指定秒のフレーム取得。前方100ms刻みで再試行し、短尺は0秒近傍を細かくなめる。
        internal static bool TryReadFrameWithRetry(
            Decoders.IThumbnailFrameSource frameSource,
            TimeSpan baseTime,
            out Bitmap frameBitmap
        )
        {
            frameBitmap = null;
            if (frameSource == null)
            {
                return false;
            }

            for (int i = 0; i <= 100; i++)
            {
                TimeSpan tryTime = baseTime + TimeSpan.FromMilliseconds(i * 100);
                if (tryTime < TimeSpan.Zero)
                {
                    tryTime = TimeSpan.Zero;
                }

                if (frameSource.TryReadFrame(tryTime, out frameBitmap))
                    {
                    return true;
                    }
                }

            // 1秒未満～1秒付近の短尺動画は、0秒起点の細かい時刻で拾えることがある。
            if (baseTime <= TimeSpan.FromSeconds(1))
                    {
                for (int ms = 0; ms <= 1000; ms += 33)
                {
                    if (frameSource.TryReadFrame(TimeSpan.FromMilliseconds(ms), out frameBitmap))
            {
                        return true;
        }

        // 3GB超コピーのユーザー許可状態を更新する。
        // 呼び出し側（MainWindow）で確認ダイアログ後に許可済みを登録する用途。
        internal static void SetLargeCopyApproval(string movieFullPath, bool approved)
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return;
            }
            string key = MovieCore.NormalizeMoviePath(movieFullPath);
            LargeCopyApprovalCache[key] = approved;
        }

            frameBitmap?.Dispose();
            frameBitmap = null;
            return false;
        }

        // 動画末尾超えを避けるための安全な最大秒を返す（dur+1防止）。
        internal static int ResolveSafeMaxCaptureSec(double durationSec)
        {
            if (durationSec <= 0 || double.IsNaN(durationSec) || double.IsInfinity(durationSec))
            {
                return 0;
            }

            // 端数や丸め誤差で末尾超えしないよう、わずかに手前へ寄せる。
            double safeEnd = Math.Max(0, durationSec - 0.001);
            return Math.Max(0, (int)Math.Floor(safeEnd));
                }

        // 動画時間と分割数から、従来規則の秒配列を構築する。
        internal static ThumbInfo BuildAutoThumbInfo(TabInfo tbi, double? durationSec)
                {
            int thumbCount = tbi.Columns * tbi.Rows;
            int divideSec = 1;
            int maxCaptureSec = int.MaxValue;
            if (durationSec.HasValue && durationSec.Value > 0)
                    {
                divideSec = (int)(durationSec.Value / (thumbCount + 1));
                if (divideSec < 1)
            {
                    divideSec = 1;
        }

                // 短尺動画でも末尾超えしないよう、安全上限で丸める。
                maxCaptureSec = ResolveSafeMaxCaptureSec(durationSec.Value);
            }

            ThumbInfo thumbInfo = new()
                    {
                ThumbWidth = tbi.Width,
                ThumbHeight = tbi.Height,
                ThumbRows = tbi.Rows,
                ThumbColumns = tbi.Columns,
                ThumbCounts = thumbCount,
                    };
                    psi.ArgumentList.Add("/c");
                    psi.ArgumentList.Add("mklink");
                    psi.ArgumentList.Add("/J");
                    psi.ArgumentList.Add(junctionDir);
                    psi.ArgumentList.Add(parentDir);

            for (int i = 1; i < thumbInfo.ThumbCounts + 1; i++)
            {
                int sec = i * divideSec;
                if (sec > maxCaptureSec)
                    {
                    sec = maxCaptureSec;
                    }
                thumbInfo.Add(sec);
                    }
            thumbInfo.NewThumbInfo();
            return thumbInfo;
                }

        // 既存互換の4:3中央トリミング矩形を返す。
        internal static Rectangle GetAspectRect(int imgWidth, int imgHeight)
                {
            int w = imgWidth;
            int h = imgHeight;
            int wdiff = 0;
            int hdiff = 0;

            float aspect = (float)imgWidth / imgHeight;
            if (aspect > 1.34f)
                {
                h = (int)Math.Floor((decimal)imgHeight / 3);
                w = (int)Math.Floor((decimal)h * 4);
                h = imgHeight;
                wdiff = (imgWidth - w) / 2;
                hdiff = 0;
            }

            if (aspect < 1.33f)
            {
                w = (int)Math.Floor((decimal)imgWidth / 4);
                h = (int)Math.Floor((decimal)w * 3);
                w = imgWidth;
                hdiff = (imgHeight - h) / 2;
                wdiff = 0;
            }
            return new Rectangle(wdiff, hdiff, w, h);
        }

        internal static Size ResolveDefaultTargetSize(Bitmap source)
                {
            int width = source.Width < 320 ? source.Width : 320;
            int height = source.Height < 240 ? source.Height : 240;

            if (width <= 0)
                {
                width = 320;
            }
            if (height <= 0)
            {
                height = 240;
            }
            return new Size(width, height);
        }

        internal static Bitmap CropBitmap(Bitmap source, Rectangle cropRect)
            {
            Rectangle bounded = Rectangle.Intersect(
                new Rectangle(0, 0, source.Width, source.Height),
                cropRect
            );
            if (bounded.Width <= 0 || bounded.Height <= 0)
                {
                bounded = new Rectangle(0, 0, source.Width, source.Height);
                }

            Bitmap cropped = new(bounded.Width, bounded.Height, PixelFormat.Format24bppRgb);
            using Graphics g = Graphics.FromImage(cropped);
            g.DrawImage(
                source,
                new Rectangle(0, 0, bounded.Width, bounded.Height),
                bounded,
                GraphicsUnit.Pixel
                    );
            return cropped;
        }

        internal static Bitmap ResizeBitmap(Bitmap source, Size targetSize)
                {
            Bitmap resized = new(targetSize.Width, targetSize.Height, PixelFormat.Format24bppRgb);
            using Graphics g = Graphics.FromImage(resized);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(source, new Rectangle(0, 0, targetSize.Width, targetSize.Height));
            return resized;
            }

        // フレーム群をタイル状に合成してJPEG保存する。
        internal static bool SaveCombinedThumbnail(
            string saveThumbFileName,
            IReadOnlyList<Bitmap> frames,
            int columns,
            int rows
        )
            {
            if (frames.Count < 1)
                {
                return false;
            }

            int total = Math.Min(frames.Count, columns * rows);
            int frameWidth = frames[0].Width;
            int frameHeight = frames[0].Height;
            if (frameWidth <= 0 || frameHeight <= 0)
            {
                return false;
        }

            string saveDir = Path.GetDirectoryName(saveThumbFileName) ?? "";
            if (!string.IsNullOrWhiteSpace(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            using Bitmap canvas = new(
                frameWidth * columns,
                frameHeight * rows,
                PixelFormat.Format24bppRgb
            );
            using Graphics g = Graphics.FromImage(canvas);
            g.Clear(Color.Black);

            for (int i = 0; i < total; i++)
        {
                int r = i / columns;
                int c = i % columns;
                Rectangle destRect = new(c * frameWidth, r * frameHeight, frameWidth, frameHeight);
                g.DrawImage(frames[i], destRect);
        }

            try
        {
                if (Path.Exists(saveThumbFileName))
            {
                    File.Delete(saveThumbFileName);
                }
                canvas.Save(saveThumbFileName, ImageFormat.Jpeg);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"thumb save failed: path='{saveThumbFileName}', err={ex.Message}");
                return false;
            }
            return Math.Truncate(frameCount / fps);
        }

        // 必要時のみShell経由で秒数を取得する（最後のフォールバック）。
        internal static double? TryGetDurationSecFromShell(string fileName)
        {
            object shellObj = null;
            object folderObj = null;
            object itemObj = null;
            try
            {
                var shellAppType = Type.GetTypeFromProgID("Shell.Application");
                if (shellAppType == null)
                {
                    return null;
                }

                shellObj = Activator.CreateInstance(shellAppType);
                if (shellObj == null)
                {
                    return null;
                }

                dynamic shell = shellObj;
                folderObj = shell.NameSpace(Path.GetDirectoryName(fileName));
                if (folderObj == null)
                {
                    return null;
                }

                dynamic folder = folderObj;
                itemObj = folder.ParseName(Path.GetFileName(fileName));
                if (itemObj == null)
                {
                    return null;
                }

                string timeString = folder.GetDetailsOf(itemObj, 27);
                if (TimeSpan.TryParse(timeString, out TimeSpan ts))
                {
                    if (ts.TotalSeconds > 0)
                    {
                        return Math.Truncate(ts.TotalSeconds);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"duration shell err = {e.Message} Movie = {fileName}");
            }
            finally
            {
                ReleaseComObject(itemObj);
                ReleaseComObject(folderObj);
                ReleaseComObject(shellObj);
            }

            return null;
        }

        private static void ReleaseComObject(object comObj)
        {
            if (comObj == null)
            {
                return;
            }
            try
            {
                if (Marshal.IsComObject(comObj))
                {
                    Marshal.FinalReleaseComObject(comObj);
                }
            }
            catch
            {
                // COM解放失敗時は処理継続を優先する。
            }
        }

        private static CachedMovieMeta GetCachedMovieMeta(string movieFullPath, out string cacheKey)
        {
            cacheKey = BuildMovieMetaCacheKey(movieFullPath);
            return MovieMetaCache.GetOrAdd(
                cacheKey,
                _ =>
                {
                    string hash = GetHashCRC32(movieFullPath);
                    return new CachedMovieMeta(hash, null);
                }
            );
        }

        private static string BuildMovieMetaCacheKey(string movieFullPath)
        {
            try
            {
                FileInfo fi = new(movieFullPath);
                if (!fi.Exists)
                {
                    return movieFullPath;
                }
                return $"{movieFullPath}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            }
            catch
            {
                return movieFullPath;
            }
        }

        private static void CacheMovieDuration(string cacheKey, string hash, double? durationSec)
        {
            if (!durationSec.HasValue || durationSec.Value <= 0)
            {
                return;
            }

            MovieMetaCache[cacheKey] = new CachedMovieMeta(hash, durationSec);
            if (MovieMetaCache.Count > MovieMetaCacheMaxCount)
            {
                MovieMetaCache.Clear();
            }
        }

        // サムネ生成の実績を1行CSVで残す。後から比較しやすい固定フォーマットを使う。
        private static void WriteThumbnailCreateProcessLog(
            string engineId,
            string movieFullPath,
            string codec,
            double? durationSec,
            long fileSizeBytes,
            string outputPath,
            bool isSuccess,
            string errorMessage
        )
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IndigoMovieManager_fork",
                    "logs"
                );
                Directory.CreateDirectory(logDir);

                string logPath = Path.Combine(logDir, ThumbnailProcessLogFileName);
                bool needsHeader = !Path.Exists(logPath) || new FileInfo(logPath).Length == 0;
                string durationText =
                    durationSec.HasValue && durationSec.Value > 0
                        ? durationSec.Value.ToString("0.###", CultureInfo.InvariantCulture)
                        : "";
                string sizeText =
                    fileSizeBytes > 0 ? fileSizeBytes.ToString(CultureInfo.InvariantCulture) : "0";
                string movieFileName = Path.GetFileName(movieFullPath) ?? "";
                string line = string.Join(
                    ",",
                    EscapeCsvValue(
                        DateTime.Now.ToString(
                            "yyyy-MM-dd HH:mm:ss.fff",
                            CultureInfo.InvariantCulture
                    )
                    ),
                    EscapeCsvValue(engineId ?? ""),
                    EscapeCsvValue(movieFileName),
                    EscapeCsvValue(codec ?? ""),
                    EscapeCsvValue(durationText),
                    EscapeCsvValue(sizeText),
                    EscapeCsvValue(outputPath ?? ""),
                    EscapeCsvValue(isSuccess ? "success" : "failed"),
                    EscapeCsvValue(errorMessage ?? "")
                        );

                lock (ThumbnailProcessLogLock)
                {
                    using StreamWriter writer = new(logPath, append: true, new UTF8Encoding(false));
                    if (needsHeader)
                    {
                        writer.WriteLine(
                            "datetime,engine,movie_file_name,codec,length_sec,size_bytes,output_path,status,error_message"
                        );
            }
                    writer.WriteLine(line);
        }
    }
            catch
    {
                // ログ失敗で本体処理を止めない。
            }
    }

        private static string EscapeCsvValue(string value)
    {
            value ??= "";
            if (
                !value.Contains(',')
                && !value.Contains('"')
                && !value.Contains('\n')
                && !value.Contains('\r')
            )
        {
                return value;
            }
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        public string InputPath { get; }
        public InputPathStage Stage { get; }
    }

    // MainWindowへ返すサムネイル生成結果。
    public sealed class ThumbnailCreateResult
    {
        public string SaveThumbFileName { get; init; } = "";
        public double? DurationSec { get; init; }
        public bool IsSuccess { get; init; }
        public string ErrorMessage { get; init; } = "";
    }

    internal sealed class CachedMovieMeta
    {
        public CachedMovieMeta(string hash, double? durationSec)
        {
            Hash = hash;
            DurationSec = durationSec;
        }

        public string Hash { get; }
        public double? DurationSec { get; }
    }
}
