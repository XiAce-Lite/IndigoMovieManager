using Force.Crc32;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;

namespace IndigoMovieManager
{
    public class Tools
    {
        /// <summary>
        /// CRC32ハッシュの取得。先頭128K分でハッシュ作る。
        /// </summary>
        /// <param name="filePath">対象のファイル</param>
        /// <returns></returns>
        public static string GetHashCRC32(string filePath = "")
        {
            var fileName = filePath;
            if (!Path.Exists(filePath)) { return ""; }

            try
            {
                int counter = 0;
                while (IsFileLocked(fileName))
                {
                    Task.Delay(100);
                    counter++;
                    if (counter > 100)
                    {
                        break;
                    }
                }
                using var reader = new BinaryReader(new FileStream(fileName, FileMode.Open, FileAccess.Read));
                var buff = reader.ReadBytes(1024 * 128);
                var algorithm = new Crc32Algorithm();
                var crc32AsBytes = algorithm.ComputeHash(buff);
                return BitConverter.ToString(crc32AsBytes, 0).Replace("-", string.Empty).ToLower();
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
        }

        private static bool IsFileLocked(string path)
        {
            FileStream stream = null;

            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch
            {
                return true;
            }
            finally
            {
                stream?.Close();
            }

            return false;
        }

        public static string ConvertTagsWithNewLine(List<string> tags)
        {
            string tagWithNewLine = "";
            IEnumerable<string> result = tags.Distinct();

            foreach (var tagItem in result)
            {
                if (string.IsNullOrEmpty(tagWithNewLine))
                {
                    tagWithNewLine = tagItem;
                }
                else
                {
                    tagWithNewLine += (Environment.NewLine + tagItem);
                }
            }
            return tagWithNewLine;
        }

        /// <summary>
        /// WhiteBrowser仕様のJpegファイルの後ろからサムネイルの情報を取得する。
        /// </summary>
        public class ThumbInfo
        {
            private int thumbCounts = 1;
            private int thumbWidth = 160;
            private int thumbHeight = 120;
            private int thumbColumns = 1;
            private int thumbRows = 1;
            private List<int> thumbSec = [];
            private bool isThumbnail = false;

            public int ThumbCounts { get { return thumbCounts; } set { thumbCounts = value; } }
            public int ThumbWidth { get { return thumbWidth; } set { thumbWidth = value; } }
            public int ThumbHeight { get { return thumbHeight; } set { thumbHeight = value; } }
            public int ThumbColumns { get { return thumbColumns; } set { thumbColumns = value; } }
            public int ThumbRows { get { return thumbRows; } set { thumbRows = value; } }
            public int TotalWidth { get { return thumbColumns * thumbWidth; } }
            public int TotalHeight { get { return thumbRows * thumbHeight; } }
            public List<int> ThumbSec { get { return thumbSec; } set { thumbSec = value; } }
            public bool IsThumbnail { get { return isThumbnail; } set { isThumbnail = value; } }
            public byte[] SecBuffer { get; set; } = [];
            public byte[] InfoBuffer { get; set; } = new byte[60];

            public void Add(int sec)
            {
                thumbSec.Add(sec);
            }

            public void NewThumbInfo()
            {
                int i = 0;
                SecBuffer = new byte[(ThumbSec.Count * 4) + 4];
                foreach (var item in ThumbSec)
                {
                    byte[] tempSecByte = BitConverter.GetBytes(item);
                    tempSecByte.CopyTo(SecBuffer, i * 4);
                    i++;
                }

                byte[] tempByte = BitConverter.GetBytes(1398033709);
                tempByte.CopyTo(SecBuffer, i * 4);

                tempByte = BitConverter.GetBytes(ThumbCounts);
                tempByte.CopyTo(InfoBuffer, 0);
                tempByte = BitConverter.GetBytes(thumbWidth);
                tempByte.CopyTo(InfoBuffer, 12);
                tempByte = BitConverter.GetBytes(thumbHeight);
                tempByte.CopyTo(InfoBuffer, 16);
                tempByte = BitConverter.GetBytes(thumbColumns);
                tempByte.CopyTo(InfoBuffer, 20);
                tempByte = BitConverter.GetBytes(ThumbRows);
                tempByte.CopyTo(InfoBuffer, 24);
            }

            public void GetThumbInfo(string fileName)
            {
                if (!Path.Exists(fileName)) { return; }
                using (FileStream src = new(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    //最終4バイトのチェックをまず。
                    src.Seek(-4, SeekOrigin.End);
                    byte[] lastBuf = new byte[4];
                    src.Read(lastBuf, 0, 4);
                    if (BitConverter.ToString(lastBuf) == "2D-4D-54-53") {
                        return;
                    }

                    //後ろ60バイトを読み込み。この中に各種情報あり。全然普通のJpgでも動いちゃうけど。
                    src.Seek(-60, SeekOrigin.End);
                    byte[] settingBuf = new byte[60];
                    src.Read(settingBuf, 0, 60);

                    var tempCount = BitConverter.ToUInt16(settingBuf[0..3], 0);

                    //一応、普通のJpg避け
                    if (tempCount > 100)  //サムネ総数100とかはねぇだろって事で。
                    {
                        return;
                    }

                    if (tempCount < 1)
                    {  //サムネ総数1未満もねぇだろって事で。
                        return;
                    }

                    thumbCounts = BitConverter.ToUInt16(settingBuf[0..3], 0);
                    thumbWidth = BitConverter.ToUInt16(settingBuf[12..15], 0);
                    thumbHeight = BitConverter.ToUInt16(settingBuf[16..19], 0);
                    thumbColumns = BitConverter.ToUInt16(settingBuf[20..23], 0);
                    thumbRows = BitConverter.ToUInt16(settingBuf[24..27], 0);
                    int[] thumbSec = new int[thumbCounts];

                    //一応後ろ512バイトを読み込み。FFD9以降に、各サムネの再生時間（秒）あり。
                    src.Seek(-512, SeekOrigin.End);
                    while (true)
                    {
                        try
                        {
                            int b = 0;
                            while ((b = src.ReadByte()) > -1)
                            {
                                if (b == 255)           //FFが出てきたら、
                                {
                                    //次のバイトを読む。
                                    var nextb = src.ReadByte();
                                    if (nextb == 217)   //D9だったら、処理開始。
                                    {
                                        var chkb = src.ReadByte();
                                        if (chkb < 0) { break; }
                                        while (chkb == 0)
                                        {
                                            chkb = src.ReadByte();
                                        }

                                        src.Seek(-1, SeekOrigin.Current);

                                        byte[] bufInf = new byte[4];
                                        //4バイト読み込み
                                        src.Read(bufInf, 0, 4);
                                        if (BitConverter.ToString(bufInf) == "2D-4D-54-53")
                                        {
                                            return;
                                        }

                                        //1つ目
                                        long sec = BitConverter.ToUInt16(bufInf, 0);
                                        int i = 0;
                                        thumbSec[i] = (int)sec;
                                        i++;

                                        //2つ目以降。2D4Dが出てきたら、処理終了。
                                        while (true)
                                        {
                                            src.Read(bufInf, 0, 4);
                                            if (BitConverter.ToString(bufInf) == "2D-4D-54-53")
                                            {
                                                break;
                                            }
                                            sec = BitConverter.ToUInt16(bufInf, 0);
                                            thumbSec[i] = (int)sec;
                                            i++;
                                        }
                                    }
                                }
                            }

                            if (b < 0)
                            {
                                break;
                            }
                        }
                        catch (Exception e)
                        {
                            MessageBox.Show(e.Message, Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }

                    foreach (var sec in thumbSec)
                    {
                        ThumbSec.Add(sec);
                    }

                    isThumbnail = true;
                };
            }
        }

        /// <summary>
        /// テンプフォルダのJpeg削除
        /// </summary>
        public static void ClearTempJpg ()
        {
            // テンプフォルダ取得
            var currentPath = Directory.GetCurrentDirectory();
            var tempPath = Path.Combine(currentPath, "temp");  //Path.GetTempPath();
            if (!Path.Exists(tempPath))
            {
                return;
            }

            // 既存テンプファイルの削除
            var oldTempFiles = Directory.GetFiles(tempPath, $"*.jpg", SearchOption.AllDirectories);
            foreach (var oldFile in oldTempFiles)
            {
                if (File.Exists(oldFile))
                {
                    File.Delete(oldFile);
                }
            }
        }

        /// <summary>
        /// サムネを並べる
        /// </summary>
        /// <param name="paths">元サムネ(テンポラリファイル)のパス群</param>
        /// <param name="columns">横のパネル数</param>
        /// <param name="rows">縦の行数</param>
        /// <returns></returns>
        public static Bitmap ConcatImages(List<string>paths, int columns, int rows)
        {
            List<Mat> dst = [];
            Mat all = new();
            for (int j = 0; j < rows; j++)
            {
                List<Mat> src = [];
                for (int i = 0; i < columns; i++)
                {
                    src.Add(new Mat(paths[i+(j * columns)]));
                }
                dst.Add(new Mat());
                Cv2.HConcat(src, dst[j]);
            }

            Cv2.VConcat(dst, all);
            return BitmapConverter.ToBitmap(all);
        }
    }
}
