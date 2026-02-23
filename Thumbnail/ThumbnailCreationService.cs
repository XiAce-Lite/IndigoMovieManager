using OpenCvSharp;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using static IndigoMovieManager.Thumbnail.Tools;

namespace IndigoMovieManager.Thumbnail
{
    // サムネイル生成の実処理（切り出し・結合・保存）をまとめるサービス。
    public sealed class ThumbnailCreationService
    {
        // 同一出力ファイルへの同時書き込みを防ぐための排他ロック。
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> OutputFileLocks = new(StringComparer.OrdinalIgnoreCase);

        // ブックマーク用の単一フレームサムネイルを作成する。
        public async Task<bool> CreateBookmarkThumbAsync(string movieFullPath, string saveThumbPath, int capturePos)
        {
            if (!Path.Exists(movieFullPath)) { return false; }

            bool created = false;
            await Task.Run(() =>
            {
                using var capture = new VideoCapture(movieFullPath);
                capture.Grab();

                var img = new Mat();
                capture.PosMsec = capturePos * 1000;
                int msecCounter = 0;
                while (capture.Read(img) == false)
                {
                    capture.PosMsec += 100;
                    if (msecCounter > 100) { break; }
                    msecCounter++;
                }

                if (img == null) { return; }
                if (img.Width == 0) { return; }
                if (img.Height == 0) { return; }

                using Mat temp = new(img, GetAspect(img.Width, img.Height));
                using Mat dst = new();
                OpenCvSharp.Size sz = new(640, 480);
                Cv2.Resize(temp, dst, sz);
                OpenCvSharp.Extensions.BitmapConverter.ToBitmap(dst).Save(saveThumbPath, ImageFormat.Jpeg);

                img.Dispose();
                capture.Dispose();
                created = true;
            });

            return created;
        }

        // 通常/手動サムネイルの作成を実行し、結果情報を返す。
        public async Task<ThumbnailCreateResult> CreateThumbAsync(
            QueueObj queueObj,
            string dbName,
            string thumbFolder,
            bool isResizeThumb,
            bool isManual = false,
            CancellationToken cts = default)
        {
            TabInfo tbi = new(queueObj.Tabindex, dbName, thumbFolder);
            var movieFullPath = queueObj.MovieFullPath;

            var hash = GetHashCRC32(movieFullPath);
            var fileBody = Path.GetFileNameWithoutExtension(movieFullPath);
            var saveThumbFileName = Path.Combine(tbi.OutPath, $"{fileBody}.#{hash}.jpg");
            var outputLock = OutputFileLocks.GetOrAdd(saveThumbFileName, _ => new SemaphoreSlim(1, 1));
            await outputLock.WaitAsync(cts);

            string jobTempPath = "";
            try
            {
                if (isManual)
                {
                    // 手動差し替えは既存サムネイルがない場合は何もしない。
                    if (!Path.Exists(saveThumbFileName))
                    {
                        return new ThumbnailCreateResult
                        {
                            SaveThumbFileName = saveThumbFileName
                        };
                    }
                }

                // 並列実行時に衝突しないよう、ジョブごとのtempディレクトリを作る。
                var tempRootPath = Path.Combine(Directory.GetCurrentDirectory(), "temp");
                if (!Path.Exists(tempRootPath))
                {
                    Directory.CreateDirectory(tempRootPath);
                }
                jobTempPath = Path.Combine(tempRootPath, $"{queueObj.MovieId}_{queueObj.Tabindex}_{Guid.NewGuid():N}");
                Directory.CreateDirectory(jobTempPath);

                if (Path.Exists(tbi.OutPath) == false)
                {
                    Directory.CreateDirectory(tbi.OutPath);
                }

                double? durationSec = null;
                if (!Path.Exists(queueObj.MovieFullPath))
                {
                    if (!Path.Exists(saveThumbFileName))
                    {
                        var noFileJpeg = Path.Combine(Directory.GetCurrentDirectory(), "Images");

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

                OpenCvSharp.Size sz = new(0, 0);
                var sw = new Stopwatch();
                try
                {
                    using var capture = new VideoCapture(queueObj.MovieFullPath);
                    capture.Grab();

                    var frameCount = capture.Get(VideoCaptureProperties.FrameCount);
                    var fps = capture.Get(VideoCaptureProperties.Fps);

                    FileInfo fi = new(queueObj.MovieFullPath);
                    string fileName = fi.FullName;
                    var shellAppType = Type.GetTypeFromProgID("Shell.Application");
                    dynamic shell = Activator.CreateInstance(shellAppType);
                    dynamic objFolder = shell.NameSpace(Path.GetDirectoryName(fileName));
                    dynamic folderItem = objFolder.ParseName(Path.GetFileName(fileName));
                    string timeString = objFolder.GetDetailsOf(folderItem, 27);

                    durationSec = Math.Truncate(frameCount / fps);

                    double durationSecFromFileInfo = 0;
                    if (TimeSpan.TryParse(timeString, out TimeSpan timeSpan))
                    {
                        durationSecFromFileInfo = timeSpan.TotalSeconds;
                    }

                    if (durationSec != durationSecFromFileInfo)
                    {
                        durationSec = durationSecFromFileInfo;
                    }

                    int divideSec = (int)(durationSec.Value / ((tbi.Columns * tbi.Rows) + 1));

                    ThumbInfo thumbInfo = new()
                    {
                        ThumbWidth = tbi.Width,
                        ThumbHeight = tbi.Height,
                        ThumbRows = tbi.Rows,
                        ThumbColumns = tbi.Columns,
                        ThumbCounts = tbi.Columns * tbi.Rows
                    };

                    if (isManual)
                    {
                        thumbInfo.GetThumbInfo(saveThumbFileName);
                        if (thumbInfo.IsThumbnail == false)
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
                        for (int i = 1; i < (thumbInfo.ThumbCounts) + 1; i++)
                        {
                            thumbInfo.Add(i * divideSec);
                        }
                    }
                    thumbInfo.NewThumbInfo();

                    List<string> paths = [];
                    bool isSuccess = true;
                    await Task.Run(() =>
                    {
                        for (int i = 0; i < thumbInfo.ThumbSec.Count; i++)
                        {
                            sw.Restart();

                            var img = new Mat();
                            capture.PosMsec = thumbInfo.ThumbSec[i] * 1000;

                            int msecCounter = 0;
                            while (capture.Read(img) == false)
                            {
                                capture.PosMsec += 100;
                                if (msecCounter > 100) { break; }
                                msecCounter++;
                            }

                            sw.Stop();
                            TimeSpan ts = sw.Elapsed;
                            if (ts.Seconds > 60) { isSuccess = false; return; }

                            if (img == null) { isSuccess = false; return; }
                            if (img.Width == 0) { isSuccess = false; return; }
                            if (img.Height == 0) { isSuccess = false; return; }

                            using Mat temp = new(img, GetAspect(img.Width, img.Height));

                            var saveFile = Path.Combine(jobTempPath, $"tn_{i:D2}.jpg");
                            if (isResizeThumb)
                            {
                                sz = new OpenCvSharp.Size { Width = tbi.Width, Height = tbi.Height };
                            }
                            else if (sz.Width == 0)
                            {
                                sz = new OpenCvSharp.Size
                                {
                                    Width = temp.Width < 320 ? temp.Width : 320,
                                    Height = temp.Height < 240 ? temp.Height : 240
                                };
                            }

                            using Mat dst = new();
                            Cv2.Resize(temp, dst, sz);
                            OpenCvSharp.Extensions.BitmapConverter.ToBitmap(dst).Save(saveFile, ImageFormat.Jpeg);

                            paths.Add(saveFile);
                            img.Dispose();
                        }
                    }, cts);

                    if (!isSuccess)
                    {
                        return new ThumbnailCreateResult
                        {
                            SaveThumbFileName = saveThumbFileName,
                            DurationSec = durationSec
                        };
                    }

                    Bitmap bmp = ConcatImages(paths, tbi.Columns, tbi.Rows);
                    if (bmp != null)
                    {
                        if (Path.Exists(saveThumbFileName))
                        {
                            File.Delete(saveThumbFileName);
                        }
                        bmp.Save(saveThumbFileName, ImageFormat.Jpeg);
                        bmp.Dispose();

                        using FileStream dest = new(saveThumbFileName, FileMode.Append, FileAccess.Write);
                        dest.Seek(0, SeekOrigin.End);
                        dest.Write(thumbInfo.SecBuffer);
                        dest.Write(thumbInfo.InfoBuffer);
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"err = {e.Message} Movie = {queueObj.MovieFullPath}");
                }

                return new ThumbnailCreateResult
                {
                    SaveThumbFileName = saveThumbFileName,
                    DurationSec = durationSec
                };
            }
            finally
            {
                // 個別ジョブtempは処理後に掃除する。
                if (!string.IsNullOrEmpty(jobTempPath) && Directory.Exists(jobTempPath))
                {
                    try
                    {
                        Directory.Delete(jobTempPath, true);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine($"temp cleanup err = {e.Message} Temp = {jobTempPath}");
                    }
                }

                outputLock.Release();
            }
        }

        // サムネイル切り出し用のトリミング矩形をアスペクト比から計算する。
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
    }

    // MainWindow側へ返すサムネイル作成結果。
    public sealed class ThumbnailCreateResult
    {
        public string SaveThumbFileName { get; init; } = "";
        public double? DurationSec { get; init; }
    }
}
