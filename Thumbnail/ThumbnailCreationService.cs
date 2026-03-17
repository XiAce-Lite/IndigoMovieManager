using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using IndigoMovieManager.Thumbnail.Engines;
using IndigoMovieManager;

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
        private readonly IThumbnailLogger logger;
        private readonly IThumbnailCreationHostRuntime hostRuntime;
        private readonly IThumbnailCreateProcessLogWriter processLogWriter;
        private readonly ThumbnailCreateWorkflowCoordinator createWorkflowCoordinator;
        public ThumbnailCreationService()
            : this(
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                FallbackThumbnailCreationHostRuntime.Instance
            ) { }

        public ThumbnailCreationService(
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger
        )
            : this(
                videoMetadataProvider,
                logger,
                FallbackThumbnailCreationHostRuntime.Instance
            ) { }

        public ThumbnailCreationService(IThumbnailCreationHostRuntime hostRuntime)
            : this(
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                hostRuntime
            ) { }

        public ThumbnailCreationService(
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IThumbnailCreationHostRuntime hostRuntime
        )
            : this(videoMetadataProvider, logger, hostRuntime, null) { }

        public ThumbnailCreationService(
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter
        )
            : this(
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                hostRuntime,
                processLogWriter
            ) { }

        public ThumbnailCreationService(
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter
        )
            : this(
                new FfMediaToolkitThumbnailGenerationEngine(),
                new FfmpegOnePassThumbnailGenerationEngine(),
                new OpenCvThumbnailGenerationEngine(),
                new FfmpegAutoGenThumbnailGenerationEngine(),
                videoMetadataProvider,
                logger,
                hostRuntime,
                processLogWriter
            ) { }

        internal ThumbnailCreationService(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine
        )
            : this(
                ffMediaToolkitEngine,
                ffmpegOnePassEngine,
                openCvEngine,
                autogenEngine,
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                FallbackThumbnailCreationHostRuntime.Instance
            ) { }

        internal ThumbnailCreationService(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine,
            IThumbnailCreationHostRuntime hostRuntime
        )
            : this(
                ffMediaToolkitEngine,
                ffmpegOnePassEngine,
                openCvEngine,
                autogenEngine,
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                hostRuntime,
                null
            ) { }

        internal ThumbnailCreationService(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine,
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter
        )
            : this(
                ffMediaToolkitEngine,
                ffmpegOnePassEngine,
                openCvEngine,
                autogenEngine,
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                hostRuntime,
                processLogWriter
            ) { }

        internal ThumbnailCreationService(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine,
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger
        )
            : this(
                ffMediaToolkitEngine,
                ffmpegOnePassEngine,
                openCvEngine,
                autogenEngine,
                videoMetadataProvider,
                logger,
                FallbackThumbnailCreationHostRuntime.Instance
            ) { }

        internal ThumbnailCreationService(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine,
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IThumbnailCreationHostRuntime hostRuntime
        )
            : this(
                ffMediaToolkitEngine,
                ffmpegOnePassEngine,
                openCvEngine,
                autogenEngine,
                videoMetadataProvider,
                logger,
                hostRuntime,
                null
            ) { }

        internal ThumbnailCreationService(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine,
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger,
            IThumbnailCreationHostRuntime hostRuntime,
            IThumbnailCreateProcessLogWriter processLogWriter
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
            IVideoMetadataProvider safeVideoMetadataProvider =
                videoMetadataProvider
                ?? throw new ArgumentNullException(nameof(videoMetadataProvider));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.hostRuntime =
                hostRuntime ?? throw new ArgumentNullException(nameof(hostRuntime));
            this.processLogWriter = processLogWriter ?? NoOpThumbnailCreateProcessLogWriter.Instance;

            ThumbnailRuntimeLog.SetLogger(this.logger);

            engineRouter = new ThumbnailEngineRouter([
                this.ffMediaToolkitEngine,
                this.ffmpegOnePassEngine,
                this.openCvEngine,
                this.autogenEngine,
            ]);
            ThumbnailEngineExecutionPolicy engineExecutionPolicy = new(
                this.ffMediaToolkitEngine,
                this.ffmpegOnePassEngine,
                this.openCvEngine,
                this.autogenEngine
            );
            ThumbnailEngineExecutionCoordinator engineExecutionCoordinator = new(
                engineExecutionPolicy
            );
            ThumbnailMovieMetaResolver movieMetaResolver = new(safeVideoMetadataProvider);
            ThumbnailCreatePreparationResolver preparationResolver = new(movieMetaResolver);
            ThumbnailJobContextBuilder jobContextBuilder = new(movieMetaResolver);
            ThumbnailCreateResultFinalizer resultFinalizer = new(
                this.processLogWriter,
                movieMetaResolver
            );
            ThumbnailPrecheckCoordinator precheckCoordinator = new(
                this.hostRuntime,
                movieMetaResolver,
                jobContextBuilder,
                resultFinalizer
            );
            createWorkflowCoordinator = new ThumbnailCreateWorkflowCoordinator(
                preparationResolver,
                precheckCoordinator,
                jobContextBuilder,
                engineRouter,
                engineExecutionPolicy,
                engineExecutionCoordinator,
                resultFinalizer
            );
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
                ThumbnailRuntimeLog.Write(
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
            CancellationToken cts = default,
            string sourceMovieFullPathOverride = null,
            string initialEngineHint = null,
            ThumbInfo thumbInfoOverride = null
        )
        {
            // 既存の QueueObj 呼び出しは残しつつ、中の本流だけ新契約へ寄せていく。
            ThumbnailRequest request = queueObj?.ToThumbnailRequest() ?? new ThumbnailRequest();
            try
            {
                return await CreateThumbAsync(
                    request,
                    dbName,
                    thumbFolder,
                    isResizeThumb,
                    isManual,
                    cts,
                    sourceMovieFullPathOverride,
                    initialEngineHint,
                    thumbInfoOverride
                );
            }
            finally
            {
                queueObj?.ApplyThumbnailRequest(request);
            }
        }

        public async Task<ThumbnailCreateResult> CreateThumbAsync(
            ThumbnailRequest request,
            string dbName,
            string thumbFolder,
            bool isResizeThumb,
            bool isManual = false,
            CancellationToken cts = default,
            string sourceMovieFullPathOverride = null,
            string initialEngineHint = null,
            ThumbInfo thumbInfoOverride = null
        )
        {
            return await createWorkflowCoordinator.ExecuteAsync(
                new ThumbnailCreateWorkflowRequest
                {
                    Request = request,
                    DbName = dbName,
                    ThumbFolder = thumbFolder,
                    IsResizeThumb = isResizeThumb,
                    IsManual = isManual,
                    SourceMovieFullPathOverride = sourceMovieFullPathOverride,
                    InitialEngineHint = initialEngineHint,
                    ThumbInfoOverride = thumbInfoOverride,
                },
                cts
            );
        }

        internal static ThumbnailCreateResult CreateSuccessResult(
            string saveThumbFileName,
            double? durationSec,
            ThumbnailPreviewFrame previewFrame = null
        )
        {
            return new ThumbnailCreateResult
            {
                SaveThumbFileName = saveThumbFileName,
                DurationSec = durationSec,
                IsSuccess = true,
                PreviewFrame = previewFrame,
            };
        }

        internal static ThumbnailCreateResult CreateFailedResult(
            string saveThumbFileName,
            double? durationSec,
            string errorMessage,
            ThumbnailPreviewFrame previewFrame = null
        )
        {
            return new ThumbnailCreateResult
            {
                SaveThumbFileName = saveThumbFileName,
                DurationSec = durationSec,
                IsSuccess = false,
                ErrorMessage = errorMessage ?? "",
                PreviewFrame = previewFrame,
            };
        }

        // エンジン内部で得たBitmapを、UI非依存のプレビューDTOへ詰め替える。
        internal static ThumbnailPreviewFrame CreatePreviewFrameFromBitmap(
            Bitmap source,
            int maxHeight = 120
        )
        {
            if (source == null || source.Width < 1 || source.Height < 1)
            {
                return null;
            }

            Size scaledSize = ResolvePreviewTargetSize(source.Size, maxHeight);
            using Bitmap normalized = new(
                scaledSize.Width,
                scaledSize.Height,
                PixelFormat.Format24bppRgb
            );
            using (Graphics g = Graphics.FromImage(normalized))
            {
                g.Clear(Color.Black);
                g.DrawImage(source, 0, 0, scaledSize.Width, scaledSize.Height);
            }

            BitmapData bitmapData = null;
            try
            {
                bitmapData = normalized.LockBits(
                    new Rectangle(0, 0, normalized.Width, normalized.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format24bppRgb
                );
                int stride = bitmapData.Stride;
                if (stride < 1)
                {
                    return null;
                }

                int pixelByteLength = stride * normalized.Height;
                if (pixelByteLength < 1)
                {
                    return null;
                }

                byte[] pixelBytes = new byte[pixelByteLength];
                Marshal.Copy(bitmapData.Scan0, pixelBytes, 0, pixelByteLength);
                return new ThumbnailPreviewFrame
                {
                    PixelBytes = pixelBytes,
                    Width = normalized.Width,
                    Height = normalized.Height,
                    Stride = stride,
                    PixelFormat = ThumbnailPreviewPixelFormat.Bgr24,
                };
            }
            catch
            {
                return null;
            }
            finally
            {
                if (bitmapData != null)
                {
                    normalized.UnlockBits(bitmapData);
                }
            }
        }

        internal static bool IsNearBlackBitmap(Bitmap source, out double averageLuma)
        {
            // 旧呼び出し口を残しつつ、判定本体は helper へ寄せる。
            return ThumbnailNearBlackDetector.IsNearBlackBitmap(source, out averageLuma);
        }

        internal static bool IsNearBlackImageFile(string imagePath, out double averageLuma)
        {
            // 旧呼び出し口を残しつつ、判定本体は helper へ寄せる。
            return ThumbnailNearBlackDetector.IsNearBlackImageFile(imagePath, out averageLuma);
        }

        // ミニパネル用途で過剰メモリを避けるため、上限高さだけ抑えて等比縮小する。
        private static Size ResolvePreviewTargetSize(Size sourceSize, int maxHeight)
        {
            if (sourceSize.Width < 1 || sourceSize.Height < 1)
            {
                return new Size(1, 1);
            }

            int safeMaxHeight = maxHeight < 1 ? sourceSize.Height : maxHeight;
            if (sourceSize.Height <= safeMaxHeight)
            {
                return sourceSize;
            }

            double scale = (double)safeMaxHeight / sourceSize.Height;
            int width = Math.Max(1, (int)Math.Round(sourceSize.Width * scale));
            return new Size(width, safeMaxHeight);
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

        // 通常生成は tabIndex から直接レイアウトを引き、TabInfo 生成を介さない。
        private static ThumbnailLayoutProfile ResolveLayoutProfile(int tabIndex)
        {
            return ThumbnailLayoutProfileResolver.Resolve(
                tabIndex,
                ThumbnailDetailModeRuntime.ReadRuntimeMode()
            );
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
        internal static ThumbInfo BuildAutoThumbInfo(
            ThumbnailLayoutProfile layoutProfile,
            double? durationSec
        )
        {
            int columns = Math.Max(1, layoutProfile?.Columns ?? 1);
            int rows = Math.Max(1, layoutProfile?.Rows ?? 1);
            int thumbCount = columns * rows;
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

            ThumbnailSheetSpec spec = new()
            {
                ThumbWidth = Math.Max(1, layoutProfile?.Width ?? 120),
                ThumbHeight = Math.Max(1, layoutProfile?.Height ?? 90),
                ThumbRows = rows,
                ThumbColumns = columns,
                ThumbCount = thumbCount,
            };

            for (int i = 1; i < thumbCount + 1; i++)
            {
                int sec = i * divideSec;
                if (sec > maxCaptureSec)
                {
                    sec = maxCaptureSec;
                }
                spec.CaptureSeconds.Add(sec);
            }
            return ThumbInfo.FromSheetSpec(spec);
        }

        // 旧経路互換のため残しているが、新規生成では原則使わない。
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

        internal static Bitmap ResizeBitmap(
            Bitmap source,
            Size targetSize,
            double? sourceDisplayAspectRatio = null
        )
        {
            Bitmap resized = new(targetSize.Width, targetSize.Height, PixelFormat.Format24bppRgb);
            using Graphics g = Graphics.FromImage(resized);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.Clear(Color.Black);

            // 固定枠の中へ元動画の比率を保ったまま収め、余白は黒で埋める。
            Rectangle drawRect = CalculateAspectFitRectangle(
                new Size(source.Width, source.Height),
                targetSize,
                sourceDisplayAspectRatio
            );
            g.DrawImage(source, drawRect);
            return resized;
        }

        internal static Rectangle CalculateAspectFitRectangle(
            Size sourceSize,
            Size targetSize,
            double? sourceDisplayAspectRatio = null
        )
        {
            if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
            {
                return new Rectangle(0, 0, Math.Max(1, targetSize.Width), Math.Max(1, targetSize.Height));
            }

            if (targetSize.Width <= 0 || targetSize.Height <= 0)
            {
                return new Rectangle(0, 0, sourceSize.Width, sourceSize.Height);
            }

            double sourceAspect = ResolveAspectRatio(sourceSize, sourceDisplayAspectRatio);
            if (sourceAspect <= 0)
            {
                sourceAspect = (double)sourceSize.Width / sourceSize.Height;
            }

            double targetAspect = (double)targetSize.Width / targetSize.Height;

            // 4:3 ぴったり素材や、SAR補正後に目標比へ一致する素材は全面へ敷く。
            if (Math.Abs(sourceAspect - targetAspect) <= 0.01d)
            {
                return new Rectangle(0, 0, targetSize.Width, targetSize.Height);
            }

            int drawWidth;
            int drawHeight;
            if (sourceAspect >= targetAspect)
            {
                drawWidth = targetSize.Width;
                drawHeight = Math.Max(1, (int)Math.Round(targetSize.Width / sourceAspect));
            }
            else
            {
                drawHeight = targetSize.Height;
                drawWidth = Math.Max(1, (int)Math.Round(targetSize.Height * sourceAspect));
            }

            int offsetX = (targetSize.Width - drawWidth) / 2;
            int offsetY = (targetSize.Height - drawHeight) / 2;
            return new Rectangle(offsetX, offsetY, drawWidth, drawHeight);
        }

        internal static double ResolveAspectRatio(Size sourceSize, double? sourceDisplayAspectRatio = null)
        {
            if (
                sourceDisplayAspectRatio.HasValue
                && sourceDisplayAspectRatio.Value > 0
                && !double.IsNaN(sourceDisplayAspectRatio.Value)
                && !double.IsInfinity(sourceDisplayAspectRatio.Value)
            )
            {
                return sourceDisplayAspectRatio.Value;
            }

            if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
            {
                return 0;
            }

            return (double)sourceSize.Width / sourceSize.Height;
        }

        internal static bool SaveCombinedThumbnail(
            string saveThumbFileName,
            IReadOnlyList<Bitmap> frames,
            int columns,
            int rows
        )
        {
            // 旧呼び出し口を残しつつ、保存本体は helper へ寄せる。
            return ThumbnailImageWriter.SaveCombinedThumbnail(
                saveThumbFileName,
                frames,
                columns,
                rows
            );
        }

        internal static bool TrySaveJpegWithRetry(Image image, string savePath, out string errorMessage)
        {
            // 旧呼び出し口を残しつつ、保存本体は helper へ寄せる。
            return ThumbnailImageWriter.TrySaveJpegWithRetry(image, savePath, out errorMessage);
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
        public string ProcessEngineId { get; set; } = "";
        public ThumbnailPreviewFrame PreviewFrame { get; init; }
    }

    /// <summary>
    /// WPF非依存でプレビュー画素を受け渡すための中立DTO。
    /// </summary>
    public sealed class ThumbnailPreviewFrame
    {
        public byte[] PixelBytes { get; init; } = [];
        public int Width { get; init; }
        public int Height { get; init; }
        public int Stride { get; init; }
        public ThumbnailPreviewPixelFormat PixelFormat { get; init; } =
            ThumbnailPreviewPixelFormat.Bgr24;

        public bool IsValid()
        {
            if (PixelBytes == null || Width < 1 || Height < 1 || Stride < 1)
            {
                return false;
            }

            long requiredLength = (long)Stride * Height;
            if (requiredLength < 1 || requiredLength > int.MaxValue)
            {
                return false;
            }

            return PixelBytes.Length >= requiredLength;
        }
    }

    public enum ThumbnailPreviewPixelFormat
    {
        Unknown = 0,
        Bgr24 = 1,
        Bgra32 = 2,
    }

}
