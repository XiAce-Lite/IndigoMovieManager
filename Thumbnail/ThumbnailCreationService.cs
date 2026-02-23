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

        // GPUデコード検証は環境変数で有効化する（off/auto/cuda/d3d11va/dxva2/qsv）。
        private const string GpuDecodeModeEnvName = "IMM_THUMB_GPU_DECODE";
        private const string OpenCvFfmpegCaptureOptionsEnvName = "OPENCV_FFMPEG_CAPTURE_OPTIONS";
        private static readonly object GpuDecodeConfigLock = new();
        private static readonly object GpuDecodeLogLock = new();
        private static string _configuredGpuDecodeMode = "";
        private static int _gpuDecodeLogDone = 0;

        // ブックマーク用の単一フレームサムネイルを生成する。
        public async Task<bool> CreateBookmarkThumbAsync(string movieFullPath, string saveThumbPath, int capturePos)
        {
            if (!Path.Exists(movieFullPath)) { return false; }

            bool created = false;
            await Task.Run(() =>
            {
                using var capture = CreateVideoCapture(movieFullPath, out string decodeMode);
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
                LogGpuDecodeRun(movieFullPath, decodeMode, 1, 0);
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
                    using var capture = CreateVideoCapture(movieFullPath, out string decodeMode);
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
                        var decodeSw = Stopwatch.StartNew();
                        int decodedFrames = 0;
                        OpenCvSharp.Size? targetSize = null;
                        bool isSuccess = true;

                        for (int i = 0; i < thumbInfo.ThumbSec.Count; i++)
                        {
                            cts.ThrowIfCancellationRequested();

                            using var img = new Mat();
                            capture.PosMsec = thumbInfo.ThumbSec[i] * 1000;

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
                            decodedFrames++;

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

                            using var dst = new Mat();
                            Cv2.Resize(cropped, dst, targetSize!.Value);
                            resizedFrames.Add(dst.Clone());
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

                        decodeSw.Stop();
                        LogGpuDecodeRun(movieFullPath, decodeMode, decodedFrames, decodeSw.ElapsedMilliseconds);
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
            return Cv2.ImWrite(saveThumbFileName, canvas);
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

        // 環境変数の指定に応じて、GPUデコード要求付きのVideoCaptureを作る。
        // 失敗時はCPUデコードへ自動フォールバックする。
        private static VideoCapture CreateVideoCapture(string movieFullPath, out string decodeMode)
        {
            string rawMode = Environment.GetEnvironmentVariable(GpuDecodeModeEnvName);
            string mode = NormalizeGpuDecodeMode(rawMode);
            if (mode == "off")
            {
                decodeMode = "cpu-default";
                LogGpuDecodeConfigOnce(mode, rawMode);
                return new VideoCapture(movieFullPath);
            }

            EnsureGpuDecodeOptionsConfigured(mode);
            LogGpuDecodeConfigOnce(mode, rawMode);

            try
            {
                var ffmpegCapture = new VideoCapture(movieFullPath, VideoCaptureAPIs.FFMPEG);
                if (ffmpegCapture.IsOpened())
                {
                    decodeMode = $"ffmpeg-{mode}-requested";
                    return ffmpegCapture;
                }

                ffmpegCapture.Dispose();
            }
            catch (Exception e)
            {
                WriteGpuDecodeLog($"gpu decode open err = {e.Message} Movie = {movieFullPath}");
            }

            decodeMode = "cpu-fallback";
            return new VideoCapture(movieFullPath);
        }

        private static string NormalizeGpuDecodeMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) { return "off"; }
            string normalized = mode.Trim().ToLowerInvariant();
            return normalized switch
            {
                "1" => "auto",
                "true" => "auto",
                "on" => "auto",
                "auto" => "auto",
                "cuda" => "cuda",
                "d3d11va" => "d3d11va",
                "dxva2" => "dxva2",
                "qsv" => "qsv",
                _ => "off"
            };
        }

        private static void EnsureGpuDecodeOptionsConfigured(string mode)
        {
            if (mode == "off") { return; }

            lock (GpuDecodeConfigLock)
            {
                if (!string.IsNullOrEmpty(_configuredGpuDecodeMode)) { return; }

                string options = mode switch
                {
                    "cuda" => "hwaccel;cuda|hwaccel_output_format;cuda",
                    "d3d11va" => "hwaccel;d3d11va",
                    "dxva2" => "hwaccel;dxva2",
                    "qsv" => "hwaccel;qsv",
                    _ => "hwaccel;auto"
                };

                string current = Environment.GetEnvironmentVariable(OpenCvFfmpegCaptureOptionsEnvName);
                if (string.IsNullOrWhiteSpace(current))
                {
                    Environment.SetEnvironmentVariable(OpenCvFfmpegCaptureOptionsEnvName, options);
                }

                _configuredGpuDecodeMode = mode;
            }
        }

        private static void LogGpuDecodeConfigOnce(string mode, string rawMode)
        {
            if (Interlocked.Exchange(ref _gpuDecodeLogDone, 1) != 0) { return; }

            string options = Environment.GetEnvironmentVariable(OpenCvFfmpegCaptureOptionsEnvName);
            WriteGpuDecodeLog($"thumbnail gpu decode mode(raw) = {rawMode}");
            WriteGpuDecodeLog($"thumbnail gpu decode mode(normalized) = {mode}");
            WriteGpuDecodeLog($"{OpenCvFfmpegCaptureOptionsEnvName} = {options}");
        }

        private static void LogGpuDecodeRun(string movieFullPath, string decodeMode, int decodedFrames, long elapsedMs)
        {
            WriteGpuDecodeLog($"thumb decode run mode={decodeMode}, frames={decodedFrames}, ms={elapsedMs}, movie={movieFullPath}");
        }

        // GPUデコード検証ログを Debug 出力とファイルへ同時に書き出す。
        private static void WriteGpuDecodeLog(string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
            Debug.WriteLine(line);

            try
            {
                var baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IndigoMovieManager",
                    "logs");
                Directory.CreateDirectory(baseDir);
                var logPath = Path.Combine(baseDir, "thumb_decode.log");

                lock (GpuDecodeLogLock)
                {
                    File.AppendAllText(logPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // ログファイル書き込みに失敗しても本処理は継続する。
            }
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
