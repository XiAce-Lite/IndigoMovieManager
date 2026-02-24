using OpenCvSharp;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using static IndigoMovieManager.Thumbnail.Tools;

namespace IndigoMovieManager.Thumbnail
{
    // サムネイル生成の重い処理（デコード・リサイズ・合成）を担当する。
    public sealed class ThumbnailCreationService
    {
        // 同一出力ファイルへの同時書き込みを防ぐ。
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> OutputFileLocks = new(StringComparer.OrdinalIgnoreCase);

        // 同一動画の再処理を軽くするため、ハッシュと動画秒数をキャッシュする。
        private static readonly ConcurrentDictionary<string, CachedMovieMeta> MovieMetaCache = new(StringComparer.OrdinalIgnoreCase);
        private const int MovieMetaCacheMaxCount = 10000;

        // デコード経路は標準 VideoCapture を使う。
        // ただし IMM_THUMB_GPU_DECODE=cuda 指定時のみ OpenCV の FFMPEG オプションへ橋渡しする。
        private const string GpuDecodeModeEnvName = "IMM_THUMB_GPU_DECODE";
        private const string OpenCvFfmpegCaptureOptionsEnvName = "OPENCV_FFMPEG_CAPTURE_OPTIONS";
        private const string CudaCaptureOptions = "hwaccel;cuda|hwaccel_output_format;cuda";
        private static readonly object GpuDecodeOptionLock = new();

        // ブックマーク用の単一フレームサムネイルを生成する。
        public async Task<bool> CreateBookmarkThumbAsync(string movieFullPath, string saveThumbPath, int capturePos)
        {
            if (!Path.Exists(movieFullPath)) { return false; }

            bool created = false;
            await Task.Run(() =>
            {
                ConfigureGpuDecodeOptionsFromEnv();
                using var capture = new VideoCapture(movieFullPath);
                if (!capture.IsOpened()) { return; }
                capture.Grab();

                using var img = new Mat();
                capture.PosMsec = capturePos * 1000;
                int msecCounter = 0;
                while (!capture.Read(img))
                {
                    capture.PosMsec += 100;
                    if (msecCounter > 100) { break; }
                    msecCounter++;
                }

                if (img.Empty()) { return; }

                using Mat temp = new(img, GetAspect(img.Width, img.Height));
                using Mat dst = new();
                OpenCvSharp.Size sz = new(640, 480);
                Cv2.Resize(temp, dst, sz);
                OpenCvSharp.Extensions.BitmapConverter.ToBitmap(dst).Save(saveThumbPath, ImageFormat.Jpeg);
                created = true;
            });

            return created;
        }

        // 通常/手動サムネイルを生成する。
        public async Task<ThumbnailCreateResult> CreateThumbAsync(
            QueueObj queueObj,
            string dbName,
            string thumbFolder,
            bool isResizeThumb,
            bool isManual = false,
            CancellationToken cts = default)
        {
            TabInfo tbi = new(queueObj.Tabindex, dbName, thumbFolder);
            string movieFullPath = queueObj.MovieFullPath;

            var cacheMeta = GetCachedMovieMeta(movieFullPath, out string cacheKey);
            string hash = cacheMeta.Hash;
            double? durationSec = cacheMeta.DurationSec;

            string fileBody = Path.GetFileNameWithoutExtension(movieFullPath);
            string saveThumbFileName = Path.Combine(tbi.OutPath, $"{fileBody}.#{hash}.jpg");
            var outputLock = OutputFileLocks.GetOrAdd(saveThumbFileName, _ => new SemaphoreSlim(1, 1));
            await outputLock.WaitAsync(cts);

            try
            {
                if (isManual && !Path.Exists(saveThumbFileName))
                {
                    // 手動更新は既存サムネイルが前提。
                    return new ThumbnailCreateResult
                    {
                        SaveThumbFileName = saveThumbFileName
                    };
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

                    return new ThumbnailCreateResult
                    {
                        SaveThumbFileName = saveThumbFileName
                    };
                }

                try
                {
                    ConfigureGpuDecodeOptionsFromEnv();
                    using var capture = new VideoCapture(movieFullPath);
                    if (!capture.IsOpened())
                    {
                        return new ThumbnailCreateResult
                        {
                            SaveThumbFileName = saveThumbFileName,
                            DurationSec = durationSec
                        };
                    }
                    capture.Grab();

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
                        if (divideSec < 1) { divideSec = 1; }
                    }

                    ThumbInfo thumbInfo = new()
                    {
                        ThumbWidth = tbi.Width,
                        ThumbHeight = tbi.Height,
                        ThumbRows = tbi.Rows,
                        ThumbColumns = tbi.Columns,
                        ThumbCounts = thumbCount
                    };

                    if (isManual)
                    {
                        thumbInfo.GetThumbInfo(saveThumbFileName);
                        if (!thumbInfo.IsThumbnail)
                        {
                            return new ThumbnailCreateResult
                            {
                                SaveThumbFileName = saveThumbFileName,
                                DurationSec = durationSec
                            };
                        }

                        if ((queueObj.ThumbPanelPos != null) && (queueObj.ThumbTimePos != null))
                        {
                            thumbInfo.ThumbSec[(int)queueObj.ThumbPanelPos] = (int)queueObj.ThumbTimePos;
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
                                if (msecCounter > 100) { break; }
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
                            else if (!targetSize.HasValue || targetSize.Value.Width == 0 || targetSize.Value.Height == 0)
                            {
                                targetSize = new OpenCvSharp.Size
                                {
                                    Width = cropped.Width < 320 ? cropped.Width : 320,
                                    Height = cropped.Height < 240 ? cropped.Height : 240
                                };
                            }

                            using Mat resized = new();
                            Cv2.Resize(cropped, resized, targetSize!.Value);
                            resizedFrames.Add(resized.Clone());
                        }

                        if (!isSuccess || resizedFrames.Count < 1)
                        {
                            return new ThumbnailCreateResult
                            {
                                SaveThumbFileName = saveThumbFileName,
                                DurationSec = durationSec
                            };
                        }

                        bool saved = SaveCombinedThumbnail(saveThumbFileName, resizedFrames, tbi.Columns, tbi.Rows);
                        if (saved)
                        {
                            using FileStream dest = new(saveThumbFileName, FileMode.Append, FileAccess.Write);
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
                catch (Exception e)
                {
                    Debug.WriteLine($"err = {e.Message} Movie = {movieFullPath}");
                }

                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = saveThumbFileName,
                    DurationSec = durationSec
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

        // フレーム群を1枚へ合成して保存する。
        private static bool SaveCombinedThumbnail(string saveThumbFileName, IReadOnlyList<Mat> frames, int columns, int rows)
        {
            if (frames.Count < 1) { return false; }
            int total = Math.Min(frames.Count, columns * rows);

            int frameWidth = frames[0].Cols;
            int frameHeight = frames[0].Rows;
            using Mat canvas = new(frameHeight * rows, frameWidth * columns, frames[0].Type(), Scalar.Black);

            for (int i = 0; i < total; i++)
            {
                int r = i / columns;
                int c = i % columns;
                var rect = new OpenCvSharp.Rect(c * frameWidth, r * frameHeight, frameWidth, frameHeight);
                using Mat roi = new(canvas, rect);
                frames[i].CopyTo(roi);
            }

            if (Path.Exists(saveThumbFileName))
            {
                File.Delete(saveThumbFileName);
            }

            // まずは通常経路。OpenCVへ最終パスを直接渡して保存する。
            // 絵文字を含まない一般的なパスはこの経路で問題なく保存できる。
            if (!HasUnmappableAnsiChar(saveThumbFileName))
            {
                return Cv2.ImWrite(saveThumbFileName, canvas);
            }

            // 絵文字など ANSI 変換できない文字を含む場合は、
            // OpenCV 側の文字列マーシャリングで例外化/失敗するため、
            // ASCII 安全な一時パスへ保存してから .NET 側で最終パスへ移動する。
            string tempDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IndigoMovieManager",
                "temp",
                "thumb-save");
            Directory.CreateDirectory(tempDir);

            string tempSavePath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.jpg");
            try
            {
                // OpenCV へは ASCII 安全な一時パスのみを渡す。
                bool tempSaved = Cv2.ImWrite(tempSavePath, canvas);
                if (!tempSaved) { return false; }

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
            if (string.IsNullOrEmpty(path)) { return false; }

            IntPtr ansiPtr = IntPtr.Zero;
            try
            {
                ansiPtr = Marshal.StringToHGlobalAnsi(path);
                return false;
            }
            catch (ArgumentException)
            {
                return true;
            }
            finally
            {
                if (ansiPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(ansiPtr);
                }
            }
        }

        // frameCount/fps から動画秒数を算出する。
        private static double? TryGetDurationSec(double frameCount, double fps)
        {
            if (fps <= 0 || double.IsNaN(fps) || double.IsInfinity(fps)) { return null; }
            if (frameCount <= 0 || double.IsNaN(frameCount) || double.IsInfinity(frameCount)) { return null; }
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
                if (shellAppType == null) { return null; }

                shellObj = Activator.CreateInstance(shellAppType);
                if (shellObj == null) { return null; }

                dynamic shell = shellObj;
                folderObj = shell.NameSpace(Path.GetDirectoryName(fileName));
                if (folderObj == null) { return null; }

                dynamic folder = folderObj;
                itemObj = folder.ParseName(Path.GetFileName(fileName));
                if (itemObj == null) { return null; }

                string timeString = folder.GetDetailsOf(itemObj, 27);
                if (TimeSpan.TryParse(timeString, out TimeSpan ts))
                {
                    if (ts.TotalSeconds > 0) { return Math.Truncate(ts.TotalSeconds); }
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
            if (comObj == null) { return; }
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
                });
        }

        private static string BuildMovieMetaCacheKey(string movieFullPath)
        {
            try
            {
                FileInfo fi = new(movieFullPath);
                if (!fi.Exists) { return movieFullPath; }
                return $"{movieFullPath}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            }
            catch
            {
                return movieFullPath;
            }
        }

        private static void CacheMovieDuration(string cacheKey, string hash, double? durationSec)
        {
            if (!durationSec.HasValue || durationSec.Value <= 0) { return; }

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
                string current = Environment.GetEnvironmentVariable(OpenCvFfmpegCaptureOptionsEnvName);
                if (useCuda)
                {
                    if (string.IsNullOrWhiteSpace(current) || string.Equals(current, CudaCaptureOptions, StringComparison.OrdinalIgnoreCase))
                    {
                        Environment.SetEnvironmentVariable(OpenCvFfmpegCaptureOptionsEnvName, CudaCaptureOptions);
                    }
                }
                else
                {
                    // このアプリが設定した値のみクリアして、他用途の独自設定は保持する。
                    if (string.Equals(current, CudaCaptureOptions, StringComparison.OrdinalIgnoreCase))
                    {
                        Environment.SetEnvironmentVariable(OpenCvFfmpegCaptureOptionsEnvName, null);
                    }
                }
            }
        }
    }

    // MainWindowへ返すサムネイル生成結果。
    public sealed class ThumbnailCreateResult
    {
        public string SaveThumbFileName { get; init; } = "";
        public double? DurationSec { get; init; }
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
