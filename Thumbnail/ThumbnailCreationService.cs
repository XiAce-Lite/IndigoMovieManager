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
    /// 【サムネイル生成の絶対的オーケストレータ】✨
    /// 状況とルールを見極め、最適な生成エンジンを召喚してサムネイルを爆誕させるぜ！🔥
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

        /// <summary>
        /// ブックマーク用のとっておきの一枚（単一フレーム）を生成する専用ルートだ！📸
        /// </summary>
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
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail",
                    $"bookmark create failed: engine={engine.EngineId}, movie='{movieFullPath}', err='{ex.Message}'"
                );
                return false;
            }
        }

        /// <summary>
        /// サムネイル生成の本丸！通常・手動を問わず、すべての生成処理はここから始まる激アツなメイン・エントリーポイントだぜ！🚀
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
                }

                if (!durationSec.HasValue || durationSec.Value <= 0)
                {
                    durationSec = TryGetDurationSecFromShell(movieFullPath);
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

                    try
                    {
                        result = await candidate.CreateAsync(context, cts);
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested)
                    {
                        // 呼び出し元キャンセル時は既存どおり中断として扱う。
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // エンジン内部例外は失敗結果へ変換して次候補へフォールバックする。
                        result = CreateFailedResult(saveThumbFileName, durationSec, ex.Message);
                    }

                    if (result.IsSuccess)
                    {
                        break;
                    }

                    if (i < engineOrder.Count - 1)
                    {
                        DebugRuntimeLog.Write(
                            "thumbnail",
                            $"engine failed: id={candidate.EngineId}, reason='{result.ErrorMessage}', try_next=True"
                        );
                    }
                }

                if (result == null)
                {
                    result = CreateFailedResult(
                        saveThumbFileName,
                        durationSec,
                        "thumbnail engine was not executed"
                    );
                }

                // ── エラーマーカー出力 ──
                // 全エンジンが失敗した場合、次回スキャンで再度「新規」と誤判定されるのを防ぐため、
                // エラーを示すダミーjpg（0バイト）を出力フォルダに配置する。
                // 手動操作時は意図的なリトライのためマーカーを作らない。
                if (!result.IsSuccess && !isManual)
                {
                    try
                    {
                        string errorMarkerPath = ThumbnailPathResolver.BuildErrorMarkerPath(
                            tbi.OutPath,
                            movieFullPath
                        );
                        if (!Path.Exists(errorMarkerPath))
                        {
                            File.WriteAllBytes(errorMarkerPath, []);
                            DebugRuntimeLog.Write(
                                "thumbnail",
                                $"error marker created: '{errorMarkerPath}'"
                            );
                        }
                    }
                    catch (Exception markerEx)
                    {
                        DebugRuntimeLog.Write(
                            "thumbnail",
                            $"error marker write failed: '{markerEx.Message}'"
                        );
                    }
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
            }
            finally
            {
                outputLock.Release();
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

        /// <summary>
        /// 自動生成時だけの特別ルール！もし今のエンジンが力尽きても、次の候補へバトンを繋いでサムネイル欠損を意地でも防ぐぜ！🏃‍♂️💨
        /// </summary>
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
            )
            {
                AddEngine(order, autogenEngine);
                AddEngine(order, ffmpegOnePassEngine);
                AddEngine(order, openCvEngine);
                return order;
            }

            if (
                string.Equals(
                    selectedEngine?.EngineId,
                    "ffmpeg1pass",
                    StringComparison.OrdinalIgnoreCase
                )
            )
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
            )
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

        /// <summary>
        /// 指定秒のフレーム探しだ！前方100ms刻みでしぶとく再試行し、短尺動画なら0秒近傍をミクロな視点で舐め回す執念のキャプチャ処理！🔎
        /// </summary>
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
                }
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

        /// <summary>
        /// 動画の時間とパネル分割数から、昔ながらの王道ルールに従ってキャプチャする秒数の配列をバッチリ構築するぜ！📐
        /// </summary>
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

        /// <summary>
        /// 集めたフレームたちを1枚のキャンバスにタイル状に美しく敷き詰め、渾身のJPEGとして保存する最終仕上げだ！🖼️✨
        /// </summary>
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

        /// <summary>
        /// サムネ生成の生きた証（実績）を1行のCSVとしてバッチリ書き残すぜ！📝
        /// 後から集計・比較しやすいようにガチガチの固定フォーマットで記録だ！
        /// </summary>
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
    }

    /// <summary>
    /// MainWindowへ凱旋報告するための、サムネイル生成結果をまとめたイケてるクラスだ！🏅
    /// </summary>
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
