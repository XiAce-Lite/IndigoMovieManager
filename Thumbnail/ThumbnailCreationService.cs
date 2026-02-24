using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using OpenCvSharp;
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
    /// </summary>
    public sealed class ThumbnailCreationService
    {
        // .NET では既定で一部コードページ（例: 932）が無効なため、
        // ANSI判定前にCodePagesプロバイダを有効化しておく。
        static ThumbnailCreationService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        // 同一出力ファイルへの同時書き込みを防ぐ。
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> OutputFileLocks = new(
            StringComparer.OrdinalIgnoreCase
        );

        // 同一動画の再処理を軽くするため、ハッシュと動画秒数をキャッシュする。
        private static readonly ConcurrentDictionary<string, CachedMovieMeta> MovieMetaCache = new(
            StringComparer.OrdinalIgnoreCase
        );
        private const int MovieMetaCacheMaxCount = 10000;

        // 3GB超コピー時はユーザー確認を挟むため、許可済みだけを保持する。
        private static readonly ConcurrentDictionary<string, bool> LargeCopyApprovalCache = new(
            StringComparer.OrdinalIgnoreCase
        );
        private const long LargeCopyConfirmThresholdBytes = 3L * 1024L * 1024L * 1024L;

        // デコード経路は標準 VideoCapture を使う。
        // ただし IMM_THUMB_GPU_DECODE=cuda 指定時のみ OpenCV の FFMPEG オプションへ橋渡しする。
        private const string GpuDecodeModeEnvName = "IMM_THUMB_GPU_DECODE";
        private const string OpenCvFfmpegCaptureOptionsEnvName = "OPENCV_FFMPEG_CAPTURE_OPTIONS";
        private const string CudaCaptureOptions = "hwaccel;cuda|hwaccel_output_format;cuda";
        private const string FfmpegExePathEnvName = "IMM_FFMPEG_EXE_PATH";
        private static readonly object GpuDecodeOptionLock = new();

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

            bool created = false;
            await Task.Run(() =>
            {
                // OpenCV向けに 1,2,3（生/短縮/別名）を順に試し、開ける入力を選ぶ。
                // 使った別名は finally で必ず掃除する。
                string tempRootDir = CreateInputPathTempRootDir("opencv-bookmark");
                try
                {
                    string moviePathForOpenCv = SelectOpenCvInputPath(
                        movieFullPath,
                        "Bookmark",
                        tempRootDir
                    );
                    if (string.IsNullOrWhiteSpace(moviePathForOpenCv))
                    {
                        return;
                    }

                    ConfigureGpuDecodeOptionsFromEnv();
                    using var capture = new VideoCapture(moviePathForOpenCv);
                    if (!capture.IsOpened())
                    {
                        LogVideoCaptureOpenFailed("Bookmark", movieFullPath, moviePathForOpenCv);
                        return;
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

            string fileBody = Path.GetFileNameWithoutExtension(movieFullPath);
            string saveThumbFileName = Path.Combine(tbi.OutPath, $"{fileBody}.#{hash}.jpg");
            var outputLock = OutputFileLocks.GetOrAdd(
                saveThumbFileName,
                _ => new SemaphoreSlim(1, 1)
            );
            await outputLock.WaitAsync(cts);

            try
            {
                if (isManual && !Path.Exists(saveThumbFileName))
                {
                    // 手動更新は既存サムネイルが前提。
                    return new ThumbnailCreateResult { SaveThumbFileName = saveThumbFileName };
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

                    return new ThumbnailCreateResult { SaveThumbFileName = saveThumbFileName };
                }

                async Task<ThumbnailCreateResult> CreateFallbackResultAsync()
                {
                    // OpenCV で開けない場合のみ、絵文字パスに強い ffmpeg へ切り替える。
                    // 既存の通常経路は温存し、失敗時だけフォールバックさせることで副作用を最小化する。
                    if (!isManual)
                    {
                        if (!durationSec.HasValue || durationSec.Value <= 0)
                        {
                            durationSec = TryGetDurationSecFromShell(movieFullPath);
                            CacheMovieDuration(cacheKey, hash, durationSec);
                        }

                        ThumbInfo fallbackThumbInfo = BuildAutoThumbInfo(tbi, durationSec);
                        FfmpegFallbackResult fallbackResult = await TryCreateThumbByFfmpegAsync(
                                movieFullPath,
                                saveThumbFileName,
                                fallbackThumbInfo,
                                tbi.Columns,
                                tbi.Rows,
                                tbi.Width,
                                tbi.Height,
                                cts
                            )
                            .ConfigureAwait(false);
                        if (fallbackResult.Saved)
                        {
                            return new ThumbnailCreateResult
                            {
                                SaveThumbFileName = saveThumbFileName,
                                DurationSec = durationSec,
                            };
                        }
                        if (fallbackResult.DeferredByLargeCopy)
                        {
                            return new ThumbnailCreateResult
                            {
                                SaveThumbFileName = saveThumbFileName,
                                DurationSec = durationSec,
                                IsDeferredByLargeCopy = true,
                                DeferredCopySizeBytes = fallbackResult.DeferredCopySizeBytes,
                            };
                        }
                    }

                    return new ThumbnailCreateResult
                    {
                        SaveThumbFileName = saveThumbFileName,
                        DurationSec = durationSec,
                    };
                }

                try
                {
                    // 【STEP 1: OpenCV 用の安全なパスを取得する】
                    // OpenCVには 1,2,3（生/短縮/別名）で通る入力を選んで渡す。
                    // 別名に使った作業フォルダは finally で必ず掃除する。
                    string openCvTempRootDir = CreateInputPathTempRootDir("opencv-queue");
                    try
                    {
                        moviePathForOpenCv = SelectOpenCvInputPath(
                            movieFullPath,
                            "QueueThumb",
                            openCvTempRootDir
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

                        int thumbCount = tbi.Columns * tbi.Rows;
                        int divideSec = 1;
                        if (durationSec.HasValue && durationSec.Value > 0)
                        {
                            divideSec = (int)(durationSec.Value / (thumbCount + 1));
                            if (divideSec < 1)
                            {
                                divideSec = 1;
                            }
                        }

                        ThumbInfo thumbInfo = new()
                        {
                            ThumbWidth = tbi.Width,
                            ThumbHeight = tbi.Height,
                            ThumbRows = tbi.Rows,
                            ThumbColumns = tbi.Columns,
                            ThumbCounts = thumbCount,
                        };

                        if (isManual)
                        {
                            thumbInfo.GetThumbInfo(saveThumbFileName);
                            if (!thumbInfo.IsThumbnail)
                            {
                                return new ThumbnailCreateResult
                                {
                                    SaveThumbFileName = saveThumbFileName,
                                    DurationSec = durationSec,
                                };
                            }

                            if ((queueObj.ThumbPanelPos != null) && (queueObj.ThumbTimePos != null))
                            {
                                thumbInfo.ThumbSec[(int)queueObj.ThumbPanelPos] = (int)
                                    queueObj.ThumbTimePos;
                            }
                        }
                        else
                        {
                            for (int i = 1; i < thumbInfo.ThumbCounts + 1; i++)
                            {
                                thumbInfo.Add(i * divideSec);
                            }
                        }
                        thumbInfo.NewThumbInfo();

                        // 【STEP 3: 目標秒数へのシークとフレーム画像の取得・リサイズ】
                        // 中間JPEGを書かず、メモリ上のMatを最後に1回だけ保存する。
                        List<Mat> resizedFrames = [];
                        try
                        {
                            OpenCvSharp.Size? targetSize = null;
                            bool isSuccess = true;

                            for (int i = 0; i < thumbInfo.ThumbSec.Count; i++)
                            {
                                cts.ThrowIfCancellationRequested();

                                capture.PosMsec = thumbInfo.ThumbSec[i] * 1000;

                                using var img = new Mat();
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
                                    isSuccess = false;
                                    break;
                                }

                                using Mat cropped = new(img, GetAspect(img.Width, img.Height));
                                if (isResizeThumb)
                                {
                                    targetSize = new OpenCvSharp.Size(tbi.Width, tbi.Height);
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

                                // Cloneを避けて、リサイズ先Matをそのまま保持する。
                                // まとめてfinallyでDisposeしてピークメモリを抑える。
                                Mat resized = new();
                                Cv2.Resize(cropped, resized, targetSize!.Value);
                                resizedFrames.Add(resized);
                            }

                            if (!isSuccess || resizedFrames.Count < 1)
                            {
                                return new ThumbnailCreateResult
                                {
                                    SaveThumbFileName = saveThumbFileName,
                                    DurationSec = durationSec,
                                };
                            }

                            // 【STEP 4: 取得した全フレーム画像を1枚に結合して保存する】
                            bool saved = SaveCombinedThumbnail(
                                saveThumbFileName,
                                resizedFrames,
                                tbi.Columns,
                                tbi.Rows
                            );
                            if (saved)
                            {
                                using FileStream dest = new(
                                    saveThumbFileName,
                                    FileMode.Append,
                                    FileAccess.Write
                                );
                                dest.Write(thumbInfo.SecBuffer);
                                dest.Write(thumbInfo.InfoBuffer);
                            }
                        }
                        finally
                        {
                            foreach (var frame in resizedFrames)
                            {
                                frame.Dispose();
                            }
                        }
                    }
                    finally
                    {
                        CleanupTempDirectory(openCvTempRootDir);
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine(
                        $"err = {e.Message} MovieRaw = {movieFullPath} MovieNormalized = {moviePathForOpenCv}"
                    );
                }

                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = saveThumbFileName,
                    DurationSec = durationSec,
                };
            }
            finally
            {
                outputLock.Release();
            }
        }

        // 4:3基準になるように中央トリミング矩形を求める。
        private static OpenCvSharp.Rect GetAspect(int imgWidth, int imgHeight)
        {
            int w = imgWidth;
            int h = imgHeight;
            int wdiff = 0;
            int hdiff = 0;

            float aspect = (float)imgWidth / imgHeight;
            if (aspect > 1.34)
            {
                h = (int)Math.Floor((decimal)imgHeight / 3);
                w = (int)Math.Floor((decimal)h * 4);
                h = imgHeight;
                wdiff = (imgWidth - w) / 2;
                hdiff = 0;
            }

            if (aspect < 1.33)
            {
                w = (int)Math.Floor((decimal)imgWidth / 4);
                h = (int)Math.Floor((decimal)w * 3);
                w = imgWidth;
                hdiff = (imgHeight - h) / 2;
                wdiff = 0;
            }
            return new OpenCvSharp.Rect(wdiff, hdiff, w, h);
        }

        /// <summary>
        /// フレーム群（複数枚の画像）をタイル状に並べて1枚へ合成し、保存する。
        /// 文字コード問題（絵文字パスなど）を回避するため、一時フォルダ経由での保存もフォールバックとして備える。
        /// </summary>
        private static bool SaveCombinedThumbnail(
            string saveThumbFileName,
            IReadOnlyList<Mat> frames,
            int columns,
            int rows
        )
        {
            if (frames.Count < 1)
            {
                return false;
            }
            int total = Math.Min(frames.Count, columns * rows);

            int frameWidth = frames[0].Cols;
            int frameHeight = frames[0].Rows;
            using Mat canvas = new(
                frameHeight * rows,
                frameWidth * columns,
                frames[0].Type(),
                Scalar.Black
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

            // まずは通常経路。OpenCVへ最終パスを直接渡して保存する。
            // ただし文字コード的に危険なパス、または直接保存で例外が出た場合はfallbackへ切り替える。
            bool requireFallback = HasUnmappableAnsiChar(saveThumbFileName);
            if (!requireFallback)
            {
                try
                {
                    bool directSaved = Cv2.ImWrite(saveThumbFileName, canvas);
                    if (directSaved)
                    {
                        return true;
                    }

                    Debug.WriteLine(
                        $"thumb save direct failed: fallback to temp path. path='{saveThumbFileName}'"
                    );
                    requireFallback = true;
                }
                catch (ArgumentException ex)
                {
                    Debug.WriteLine(
                        $"thumb save direct marshal failed: fallback to temp path. err={ex.Message}"
                    );
                    requireFallback = true;
                }
            }

            if (!requireFallback)
            {
                return false;
            }

            // 絵文字など ANSI 変換できない文字を含む場合は、
            // OpenCV 側の文字列マーシャリングで例外化/失敗するため、
            // ASCII 安全な一時パスへ保存してから .NET 側で最終パスへ移動する。
            string tempDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IndigoMovieManager_fork",
                "temp",
                "thumb-save"
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
                // 途中失敗時もテンポラリが残らないように後始末する。
                if (Path.Exists(tempSavePath))
                {
                    File.Delete(tempSavePath);
                }
            }
        }

        // OpenCV へ渡す文字列が ANSI へ変換可能か判定する。
        // 変換不可（例: 絵文字）の場合は true を返し、保存fallbackを使う。
        private static bool HasUnmappableAnsiChar(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                // 既定ANSIコードページへ「例外フォールバック」で変換し、
                // 1文字でも変換不能なら即例外にして検出する。
                int ansiCodePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
                Encoding strictAnsi = Encoding.GetEncoding(
                    ansiCodePage,
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback
                );
                _ = strictAnsi.GetBytes(path);
                return false;
            }
            catch (EncoderFallbackException)
            {
                return true;
            }
            catch (ArgumentException)
            {
                // コードページ取得失敗など保守的に true 扱いにして安全側へ倒す。
                return true;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetShortPathName(
            string lpszLongPath,
            StringBuilder lpszShortPath,
            uint cchBuffer
        );

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(
            string lpFileName,
            string lpExistingFileName,
            IntPtr lpSecurityAttributes
        );

        // 自動生成時の秒数配列を従来ロジックと同じ規則で構築する。
        // OpenCV経路とffmpeg経路でサムネイル分割位置を一致させるための共通化。
        private static ThumbInfo BuildAutoThumbInfo(TabInfo tbi, double? durationSec)
        {
            int thumbCount = tbi.Columns * tbi.Rows;
            int divideSec = 1;
            if (durationSec.HasValue && durationSec.Value > 0)
            {
                divideSec = (int)(durationSec.Value / (thumbCount + 1));
                if (divideSec < 1)
                {
                    divideSec = 1;
                }
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
                thumbInfo.Add(i * divideSec);
            }
            thumbInfo.NewThumbInfo();
            return thumbInfo;
        }

        // OpenCV/ffmpeg 共通の入力パス候補（軽い手段）を作る。
        // ここでは Raw/Short のみを返し、重い別名作成は必要時まで遅延する。
        private static List<LibraryInputCandidate> BuildNoCopyInputCandidates(string movieFullPath)
        {
            List<LibraryInputCandidate> candidates = [];
            if (string.IsNullOrWhiteSpace(movieFullPath) || !Path.Exists(movieFullPath))
            {
                return candidates;
            }

            TryAddInputCandidate(candidates, movieFullPath, InputPathStage.Raw);

            string shortPath = TryGetShortPath(movieFullPath);
            TryAddInputCandidate(candidates, shortPath, InputPathStage.ShortPath);

            return candidates;
        }

        // Raw/Short で開けなかった場合だけ、別名候補（ジャンクション/ハードリンク）を作る。
        private static List<LibraryInputCandidate> BuildAliasInputCandidates(
            string movieFullPath,
            string tempRootDir
        )
        {
            List<LibraryInputCandidate> candidates = [];
            if (string.IsNullOrWhiteSpace(movieFullPath) || !Path.Exists(movieFullPath))
            {
                return candidates;
            }

            string junctionPath = TryCreateJunctionAliasPath(movieFullPath, tempRootDir);
            TryAddInputCandidate(candidates, junctionPath, InputPathStage.JunctionAlias);

            string hardLinkPath = TryCreateHardLinkAliasPath(movieFullPath, tempRootDir);
            TryAddInputCandidate(candidates, hardLinkPath, InputPathStage.HardLinkAlias);

            return candidates;
        }

        // 同じファイルを複数候補へ重複登録しないための共通追加処理。
        private static void TryAddInputCandidate(
            List<LibraryInputCandidate> candidates,
            string inputPath,
            InputPathStage stage
        )
        {
            if (string.IsNullOrWhiteSpace(inputPath) || !Path.Exists(inputPath))
            {
                return;
            }

            foreach (var item in candidates)
            {
                if (string.Equals(item.InputPath, inputPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            candidates.Add(new LibraryInputCandidate(inputPath, stage));
        }

        /// <summary>
        /// OpenCV が動画を開けるように、パスの表記を工夫して試行する。
        /// 1. 生パス (Raw)
        /// 2. 短いパス (ShortPath - 8.3形式)
        /// 3. ジャンクション/ハードリンク (ASCIIのみで構成された仮のパス)
        /// の順に試し、最初に開けたパスを返す。
        /// </summary>
        private static string SelectOpenCvInputPath(
            string movieFullPath,
            string context,
            string tempRootDir
        )
        {
            List<LibraryInputCandidate> candidates = BuildNoCopyInputCandidates(movieFullPath);
            foreach (var candidate in candidates)
            {
                LogOpenCvPathMapping(context, movieFullPath, candidate.InputPath);
                Debug.WriteLine(
                    $"thumb opencv input try [{context}] stage={candidate.Stage}, path='{candidate.InputPath}'"
                );

                ConfigureGpuDecodeOptionsFromEnv();
                using var probe = new VideoCapture(candidate.InputPath);
                if (probe.IsOpened())
                {
                    Debug.WriteLine(
                        $"thumb opencv input selected [{context}] stage={candidate.Stage}, path='{candidate.InputPath}'"
                    );
                    return candidate.InputPath;
                }

                LogVideoCaptureOpenFailed(context, movieFullPath, candidate.InputPath);
            }

            // Raw/Shortで開けない場合のみ、別名作成コストをかける。
            List<LibraryInputCandidate> aliasCandidates = BuildAliasInputCandidates(
                movieFullPath,
                tempRootDir
            );
            foreach (var candidate in aliasCandidates)
            {
                LogOpenCvPathMapping(context, movieFullPath, candidate.InputPath);
                Debug.WriteLine(
                    $"thumb opencv input try [{context}] stage={candidate.Stage}, path='{candidate.InputPath}'"
                );

                ConfigureGpuDecodeOptionsFromEnv();
                using var probe = new VideoCapture(candidate.InputPath);
                if (probe.IsOpened())
                {
                    Debug.WriteLine(
                        $"thumb opencv input selected [{context}] stage={candidate.Stage}, path='{candidate.InputPath}'"
                    );
                    return candidate.InputPath;
                }

                LogVideoCaptureOpenFailed(context, movieFullPath, candidate.InputPath);
            }

            return "";
        }

        /// <summary>
        /// OpenCVでどうしても開けない動画（絵文字パスでのハードリンク失敗時など）に対する最終手段。
        /// ffmpeg.exe を呼び出して必要なフレーム秒数の画像を切り出し、
        /// それらを読み込んで OpenCV の形式(Mat)にしてから既存の結合処理(SaveCombinedThumbnail)へ流す。
        /// </summary>
        private static async Task<FfmpegFallbackResult> TryCreateThumbByFfmpegAsync(
            string movieFullPath,
            string saveThumbFileName,
            ThumbInfo thumbInfo,
            int columns,
            int rows,
            int targetWidth,
            int targetHeight,
            CancellationToken cts
        )
        {
            string ffmpegExePath = ResolveFfmpegExecutablePath();
            if (string.IsNullOrEmpty(ffmpegExePath))
            {
                Debug.WriteLine("thumb ffmpeg fallback skipped: ffmpeg path is not configured.");
                return FfmpegFallbackResult.Failed();
            }

            string tempRootDir = CreateInputPathTempRootDir("ffmpeg");
            try
            {
                int attemptIndex = 0;

                // まず軽い手段（Raw/Short）だけ試す。
                List<LibraryInputCandidate> candidates = BuildNoCopyInputCandidates(movieFullPath);
                for (int i = 0; i < candidates.Count; i++)
                {
                    cts.ThrowIfCancellationRequested();
                    LibraryInputCandidate candidate = candidates[i];
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
                        Debug.WriteLine(
                            $"thumb ffmpeg fallback succeeded: stage={candidate.Stage}, path='{candidate.InputPath}'"
                        );
                        return FfmpegFallbackResult.Succeeded();
                    }
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
                    string.IsNullOrWhiteSpace(copiedInput.InputPath)
                    || !Path.Exists(copiedInput.InputPath)
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
                // ここで別名（ジャンクション/ハードリンク）とコピーをまとめて掃除する。
                CleanupTempDirectory(tempRootDir);
            }
        }

        // ffmpeg本体処理。入力パスは呼び出し側で決定済みのものを受け取る。
        private static async Task<bool> TryCreateThumbByFfmpegCoreAsync(
            string ffmpegExePath,
            string ffmpegInputPath,
            string workDir,
            string saveThumbFileName,
            ThumbInfo thumbInfo,
            int columns,
            int rows,
            int targetWidth,
            int targetHeight,
            CancellationToken cts
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
                        frame.Dispose();
                        Debug.WriteLine(
                            $"thumb ffmpeg fallback failed: empty frame '{framePath}'."
                        );
                        return false;
                    }
                    resizedFrames.Add(frame);
                }

                bool saved = SaveCombinedThumbnail(saveThumbFileName, resizedFrames, columns, rows);
                if (!saved)
                {
                    return false;
                }

                using FileStream dest = new(saveThumbFileName, FileMode.Append, FileAccess.Write);
                dest.Write(thumbInfo.SecBuffer);
                dest.Write(thumbInfo.InfoBuffer);
                return true;
            }
            finally
            {
                foreach (var frame in resizedFrames)
                {
                    frame.Dispose();
                }
            }
        }

        // 最後の手段（段階4）としてコピー入力を作る。
        // 3GB超はここでDeferredを返し、呼び出し側の後回し確認へ渡す。
        private static FfmpegInputPreparationResult PrepareCopiedInputPathForFallback(
            string movieFullPath,
            string tempRootDir
        )
        {
            if (string.IsNullOrWhiteSpace(movieFullPath) || !Path.Exists(movieFullPath))
            {
                return FfmpegInputPreparationResult.Failed();
            }

            try
            {
                long fileSize = new FileInfo(movieFullPath).Length;
                if (
                    fileSize > LargeCopyConfirmThresholdBytes
                    && !IsLargeCopyApproved(movieFullPath)
                )
                {
                    Debug.WriteLine(
                        $"thumb ffmpeg fallback deferred: copy requires confirmation size={fileSize} path='{movieFullPath}'."
                    );
                    return FfmpegInputPreparationResult.Deferred(fileSize);
                }

                string ext = Path.GetExtension(movieFullPath);
                string inputCopyPath = Path.Combine(tempRootDir, $"input-copy{ext}");
                File.Copy(movieFullPath, inputCopyPath, true);
                Debug.WriteLine(
                    $"thumb ffmpeg fallback: copied input to temp path '{inputCopyPath}'."
                );
                return FfmpegInputPreparationResult.Ready(inputCopyPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"thumb ffmpeg fallback failed: input copy exception: {ex.Message}"
                );
                return FfmpegInputPreparationResult.Failed();
            }
        }

        // 問題ライブラリ向け入力処理で使う作業フォルダを作る。
        private static string CreateInputPathTempRootDir(string mode)
        {
            string tempRootDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IndigoMovieManager_fork",
                "temp",
                $"thumb-{mode}",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(tempRootDir);
            return tempRootDir;
        }

        // 別名（ジャンクション/ハードリンク）やコピーを必ず消す。
        private static void CleanupTempDirectory(string tempRootDir)
        {
            if (string.IsNullOrWhiteSpace(tempRootDir))
            {
                return;
            }

            try
            {
                if (!Directory.Exists(tempRootDir))
                {
                    return;
                }

                // 先に直下ファイルを消す（読み取り専用属性があっても消せるように補正）。
                foreach (
                    string filePath in Directory.EnumerateFiles(
                        tempRootDir,
                        "*",
                        SearchOption.TopDirectoryOnly
                    )
                )
                {
                    try
                    {
                        File.SetAttributes(filePath, FileAttributes.Normal);
                        File.Delete(filePath);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"thumb cleanup file failed: path='{filePath}', err={ex.Message}"
                        );
                    }
                }

                // 再解析ポイント（ジャンクション）を辿らないように個別削除する。
                foreach (
                    string dirPath in Directory.EnumerateDirectories(
                        tempRootDir,
                        "*",
                        SearchOption.TopDirectoryOnly
                    )
                )
                {
                    try
                    {
                        FileAttributes attrs = File.GetAttributes(dirPath);
                        bool isReparsePoint = (attrs & FileAttributes.ReparsePoint) != 0;
                        if (isReparsePoint)
                        {
                            Directory.Delete(dirPath, false);
                        }
                        else
                        {
                            Directory.Delete(dirPath, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"thumb cleanup dir failed: path='{dirPath}', err={ex.Message}"
                        );
                    }
                }

                if (Directory.Exists(tempRootDir))
                {
                    Directory.Delete(tempRootDir, false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"thumb cleanup failed: path='{tempRootDir}', err={ex.Message}");
            }
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

        private static bool IsLargeCopyApproved(string movieFullPath)
        {
            string key = MovieCore.NormalizeMoviePath(movieFullPath);
            return LargeCopyApprovalCache.TryGetValue(key, out bool approved) && approved;
        }

        // ファイルの短縮名を取得する。取得できない環境では空文字を返す。
        private static string TryGetShortPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            try
            {
                StringBuilder buffer = new(260);
                uint result = GetShortPathName(path, buffer, (uint)buffer.Capacity);
                if (result == 0)
                {
                    return "";
                }

                if (result > buffer.Capacity)
                {
                    buffer = new StringBuilder((int)result);
                    result = GetShortPathName(path, buffer, (uint)buffer.Capacity);
                    if (result == 0)
                    {
                        return "";
                    }
                }

                return buffer.ToString();
            }
            catch
            {
                return "";
            }
        }

        // 絵文字を含むディレクトリだけをASCII名へ逃がすため、ジャンクションを作る。
        // 失敗時は空文字を返し、次の手段へフォールバックする。
        private static string TryCreateJunctionAliasPath(string movieFullPath, string tempRootDir)
        {
            string parentDir = Path.GetDirectoryName(movieFullPath) ?? "";
            if (string.IsNullOrWhiteSpace(parentDir) || !Directory.Exists(parentDir))
            {
                return "";
            }

            string junctionDir = Path.Combine(tempRootDir, "input-dir");
            try
            {
                if (!Directory.Exists(junctionDir))
                {
                    ProcessStartInfo psi = new()
                    {
                        FileName = "cmd.exe",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                    };
                    psi.ArgumentList.Add("/c");
                    psi.ArgumentList.Add("mklink");
                    psi.ArgumentList.Add("/J");
                    psi.ArgumentList.Add(junctionDir);
                    psi.ArgumentList.Add(parentDir);

                    using Process process = new() { StartInfo = psi };
                    if (!process.Start())
                    {
                        return "";
                    }
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        return "";
                    }
                }

                string fileName = Path.GetFileName(movieFullPath);
                string candidate = Path.Combine(junctionDir, fileName);
                if (!Path.Exists(candidate))
                {
                    return "";
                }

                // ファイル名側にも絵文字がある場合は短縮名が取れるかを試す。
                if (!HasUnmappableAnsiChar(candidate))
                {
                    return candidate;
                }
                string shortCandidate = TryGetShortPath(candidate);
                if (!string.IsNullOrWhiteSpace(shortCandidate) && Path.Exists(shortCandidate))
                {
                    return shortCandidate;
                }
            }
            catch
            {
                // ジャンクション作成失敗は通常フローでフォールバックさせる。
            }

            return "";
        }

        // 同一ボリュームで使える場合は、ハードリンクを作ってコピーを回避する。
        private static string TryCreateHardLinkAliasPath(string movieFullPath, string tempRootDir)
        {
            try
            {
                string ext = Path.GetExtension(movieFullPath);
                string hardLinkPath = Path.Combine(tempRootDir, $"input-link{ext}");
                if (Path.Exists(hardLinkPath))
                {
                    File.Delete(hardLinkPath);
                }

                bool linked = CreateHardLink(hardLinkPath, movieFullPath, IntPtr.Zero);
                if (!linked)
                {
                    return "";
                }
                if (!Path.Exists(hardLinkPath))
                {
                    return "";
                }
                return hardLinkPath;
            }
            catch
            {
                return "";
            }
        }

        // ffmpegで1フレームだけ抽出する。
        // scale+padで出力サイズを固定し、後段結合時のサイズ不一致を防ぐ。
        private static async Task<bool> TryExtractSingleFrameByFfmpegAsync(
            string ffmpegExePath,
            string movieFullPath,
            int sec,
            int targetWidth,
            int targetHeight,
            string outputFramePath,
            CancellationToken cts
        )
        {
            ProcessStartInfo psi = new()
            {
                FileName = ffmpegExePath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            // -ss は入力前に置き、単一フレーム抽出を軽くする。
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-ss");
            psi.ArgumentList.Add(sec.ToString());
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(movieFullPath);
            psi.ArgumentList.Add("-frames:v");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-vf");
            psi.ArgumentList.Add(
                $"scale={targetWidth}:{targetHeight}:force_original_aspect_ratio=decrease,pad={targetWidth}:{targetHeight}:(ow-iw)/2:(oh-ih)/2:black"
            );
            psi.ArgumentList.Add(outputFramePath);

            try
            {
                using Process process = new() { StartInfo = psi };
                if (!process.Start())
                {
                    Debug.WriteLine("thumb ffmpeg fallback failed: process start returned false.");
                    return false;
                }

                string stdErr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                await process.WaitForExitAsync(cts).ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    Debug.WriteLine(
                        $"thumb ffmpeg fallback failed: exit={process.ExitCode}, err={stdErr}"
                    );
                    return false;
                }
                return Path.Exists(outputFramePath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"thumb ffmpeg fallback exception: {ex.Message}");
                return false;
            }
        }

        // ffmpeg実行ファイルの解決順:
        // 1) IMM_FFMPEG_EXE_PATH で明示
        // 2) アプリ配下の同梱 ffmpeg.exe
        // 3) PATH 上の ffmpeg
        private static string ResolveFfmpegExecutablePath()
        {
            string configuredPath = Environment.GetEnvironmentVariable(FfmpegExePathEnvName);
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                // 環境変数に "C:\xxx\ffmpeg.exe" 形式で入っていても扱えるように整形する。
                string normalizedConfiguredPath = configuredPath.Trim().Trim('"');
                if (Path.Exists(normalizedConfiguredPath))
                {
                    return normalizedConfiguredPath;
                }
            }

            // 同梱運用向けに、実行フォルダ直下とサブフォルダを順番に探す。
            // これで環境変数設定なしでも ffmpeg.exe を同梱するだけで動く。
            string baseDir = AppContext.BaseDirectory;
            string[] bundledCandidates =
            [
                Path.Combine(baseDir, "ffmpeg.exe"),
                Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe"),
                Path.Combine(baseDir, "tools", "ffmpeg", "ffmpeg.exe"),
                Path.Combine(baseDir, "runtimes", "win-x64", "native", "ffmpeg.exe"),
                Path.Combine(baseDir, "runtimes", "win-x86", "native", "ffmpeg.exe"),
            ];

            foreach (string candidate in bundledCandidates)
            {
                if (Path.Exists(candidate))
                {
                    return candidate;
                }
            }

            return "ffmpeg";
        }

        // OpenCV呼び出し前に、生パスと正規化パスの差分を記録する。
        // 普段はノイズを抑えるため、差分やANSI変換不可がある場合のみ出力する。
        private static void LogOpenCvPathMapping(
            string context,
            string rawPath,
            string normalizedPath
        )
        {
            string safeRaw = rawPath ?? "";
            string safeNormalized = normalizedPath ?? "";
            bool changed = !string.Equals(safeRaw, safeNormalized, StringComparison.Ordinal);
            bool rawUnmappable = HasUnmappableAnsiChar(safeRaw);
            bool normalizedUnmappable = HasUnmappableAnsiChar(safeNormalized);
            if (!changed && !rawUnmappable && !normalizedUnmappable)
            {
                return;
            }

            Debug.WriteLine(
                $"thumb path map [{context}] changed={changed}, "
                    + $"raw_unmappable={rawUnmappable}, normalized_unmappable={normalizedUnmappable}, "
                    + $"raw='{safeRaw}', normalized='{safeNormalized}'"
            );
        }

        // VideoCaptureのopen失敗時は、存在判定込みで必ずログを残す。
        // 「パス文字列問題」か「ファイル自体の問題」かを1回のログで見分けるための補助。
        private static void LogVideoCaptureOpenFailed(
            string context,
            string rawPath,
            string normalizedPath
        )
        {
            string safeRaw = rawPath ?? "";
            string safeNormalized = normalizedPath ?? "";
            bool rawExists = Path.Exists(safeRaw);
            bool normalizedExists = Path.Exists(safeNormalized);

            Debug.WriteLine(
                $"thumb capture open failed [{context}] "
                    + $"raw_exists={rawExists}, normalized_exists={normalizedExists}, "
                    + $"raw='{safeRaw}', normalized='{safeNormalized}'"
            );
        }

        // frameCount/fps から動画秒数を算出する。
        private static double? TryGetDurationSec(double frameCount, double fps)
        {
            if (fps <= 0 || double.IsNaN(fps) || double.IsInfinity(fps))
            {
                return null;
            }
            if (frameCount <= 0 || double.IsNaN(frameCount) || double.IsInfinity(frameCount))
            {
                return null;
            }
            return Math.Truncate(frameCount / fps);
        }

        // 必要時のみShell経由で秒数を取得する（最後のフォールバック）。
        private static double? TryGetDurationSecFromShell(string fileName)
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

        // IMM_THUMB_GPU_DECODE の値に合わせて OpenCV 側の hwaccel 指定を更新する。
        private static void ConfigureGpuDecodeOptionsFromEnv()
        {
            string mode = Environment.GetEnvironmentVariable(GpuDecodeModeEnvName)?.Trim();
            bool useCuda = string.Equals(mode, "cuda", StringComparison.OrdinalIgnoreCase);

            lock (GpuDecodeOptionLock)
            {
                string current = Environment.GetEnvironmentVariable(
                    OpenCvFfmpegCaptureOptionsEnvName
                );
                if (useCuda)
                {
                    if (
                        string.IsNullOrWhiteSpace(current)
                        || string.Equals(
                            current,
                            CudaCaptureOptions,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        Environment.SetEnvironmentVariable(
                            OpenCvFfmpegCaptureOptionsEnvName,
                            CudaCaptureOptions
                        );
                    }
                }
                else
                {
                    // このアプリが設定した値のみクリアして、他用途の独自設定は保持する。
                    if (
                        string.Equals(
                            current,
                            CudaCaptureOptions,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        Environment.SetEnvironmentVariable(OpenCvFfmpegCaptureOptionsEnvName, null);
                    }
                }
            }
        }
    }

    // 問題ライブラリに渡す入力パス候補の段階。
    internal enum InputPathStage
    {
        Raw = 1,
        ShortPath = 2,
        JunctionAlias = 3,
        HardLinkAlias = 4,
        Copied = 5,
    }

    // 候補パスと、その由来段階をセットで保持する。
    internal sealed class LibraryInputCandidate
    {
        public LibraryInputCandidate(string inputPath, InputPathStage stage)
        {
            InputPath = inputPath ?? "";
            Stage = stage;
        }

        public string InputPath { get; }
        public InputPathStage Stage { get; }
    }

    // MainWindowへ返すサムネイル生成結果。
    public sealed class ThumbnailCreateResult
    {
        public string SaveThumbFileName { get; init; } = "";
        public double? DurationSec { get; init; }
        public bool IsDeferredByLargeCopy { get; init; }
        public long? DeferredCopySizeBytes { get; init; }
    }

    // ffmpegフォールバック処理の結果を表す。
    // Saved=true なら保存完了、DeferredByLargeCopy=true なら後段確認待ち。
    internal sealed class FfmpegFallbackResult
    {
        public bool Saved { get; init; }
        public bool DeferredByLargeCopy { get; init; }
        public long? DeferredCopySizeBytes { get; init; }

        public static FfmpegFallbackResult Succeeded()
        {
            return new FfmpegFallbackResult { Saved = true };
        }

        public static FfmpegFallbackResult Failed()
        {
            return new FfmpegFallbackResult { Saved = false };
        }

        public static FfmpegFallbackResult Deferred(long? copySizeBytes)
        {
            return new FfmpegFallbackResult
            {
                Saved = false,
                DeferredByLargeCopy = true,
                DeferredCopySizeBytes = copySizeBytes,
            };
        }
    }

    // ffmpeg入力パス準備の状態。
    // InputPath が埋まっていれば即実行、DeferredByLargeCopy なら確認待ち。
    internal sealed class FfmpegInputPreparationResult
    {
        public string InputPath { get; init; } = "";
        public bool DeferredByLargeCopy { get; init; }
        public long? DeferredCopySizeBytes { get; init; }

        public static FfmpegInputPreparationResult Ready(string inputPath)
        {
            return new FfmpegInputPreparationResult { InputPath = inputPath ?? "" };
        }

        public static FfmpegInputPreparationResult Failed()
        {
            return new FfmpegInputPreparationResult { InputPath = "" };
        }

        public static FfmpegInputPreparationResult Deferred(long? copySizeBytes)
        {
            return new FfmpegInputPreparationResult
            {
                InputPath = "",
                DeferredByLargeCopy = true,
                DeferredCopySizeBytes = copySizeBytes,
            };
        }
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
