using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 【サムネイルのメタデータ（付加情報）管理】
    /// サムネイル画像（JPEG）の末尾に、元の動画の「どの秒数のシーンを切り取ったか」などの情報を隠して保存・復元するクラスです。
    /// ※旧名作ソフト「WhiteBrowser」互換のフォーマットを採用しており、ファイル単体でメタデータを持ち運べるのが最大の特徴です。
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

        public int ThumbCounts
        {
            get { return thumbCounts; }
            set { thumbCounts = value; }
        }
        public int ThumbWidth
        {
            get { return thumbWidth; }
            set { thumbWidth = value; }
        }
        public int ThumbHeight
        {
            get { return thumbHeight; }
            set { thumbHeight = value; }
        }
        public int ThumbColumns
        {
            get { return thumbColumns; }
            set { thumbColumns = value; }
        }
        public int ThumbRows
        {
            get { return thumbRows; }
            set { thumbRows = value; }
        }
        public int TotalWidth
        {
            get { return thumbColumns * thumbWidth; }
        }
        public int TotalHeight
        {
            get { return thumbRows * thumbHeight; }
        }
        public List<int> ThumbSec
        {
            get { return thumbSec; }
            set { thumbSec = value; }
        }
        public bool IsThumbnail
        {
            get { return isThumbnail; }
            set { isThumbnail = value; }
        }
        public byte[] SecBuffer { get; set; } = [];
        public byte[] InfoBuffer { get; set; } = new byte[60];

        public void Add(int sec)
        {
            thumbSec.Add(sec);
        }

        /// <summary>
        /// 【情報構築フロー】
        /// 新規にサムネイルを作成した際、JPEG画像の末尾にくっつけるためのバイナリデータ（バイト配列）を生成します。
        /// 先に「各フレームの秒数配列」、後ろに「分割数や画像サイズの固定長データ」というWhiteBrowser仕様のフォーマットに従います。
        /// </summary>
        public void NewThumbInfo()
        {
            // WhiteBrowser互換の末尾メタ情報を構築する。
            // 先に秒数配列、後ろに固定長の設定情報を書き出す。
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

        /// <summary>
        /// 【情報復元フロー】
        /// 既存のサムネイル画像（JPEG）の末尾をバイナリとして読み込み、
        /// 画素データの後ろに隠されている「分割数やそれぞれの取得秒数（シーク位置）」を復元してクラス変数に格納します。
        /// 再生時にサムネイルをクリックした際、そのシーンから正確に再生開始するために使われます。
        /// </summary>
        public void GetThumbInfo(string fileName)
        {
            // 既存サムネイルJPG末尾から、分割秒数とレイアウト情報を復元する。
            if (!Path.Exists(fileName))
            {
                return;
            }
            using (FileStream src = new(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                //最終4バイトのチェックをまず。
                src.Seek(-4, SeekOrigin.End);
                byte[] lastBuf = new byte[4];
                src.Read(lastBuf, 0, 4);
                if (BitConverter.ToString(lastBuf) == "2D-4D-54-53")
                {
                    return;
                }

                //後ろ60バイトを読み込み。この中に各種情報あり。全然普通のJpgでも動いちゃうけど。
                src.Seek(-60, SeekOrigin.End);
                byte[] settingBuf = new byte[60];
                src.Read(settingBuf, 0, 60);

                var tempCount = BitConverter.ToUInt16(settingBuf[0..3], 0);

                //一応、普通のJpg避け
                if (tempCount > 100) //サムネ総数100とかはねぇだろって事で。
                {
                    return;
                }

                if (tempCount < 1)
                { //サムネ総数1未満もねぇだろって事で。
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
                            if (b == 255) //FFが出てきたら、
                            {
                                //次のバイトを読む。
                                var nextb = src.ReadByte();
                                if (nextb == 217) //D9だったら、処理開始。
                                {
                                    var chkb = src.ReadByte();
                                    if (chkb < 0)
                                    {
                                        break;
                                    }
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
                        ThumbnailRuntimeLog.Write(
                            "thumbnail",
                            $"thumb info parse failed: file='{fileName}', err='{e.Message}'"
                        );
                        return;
                    }
                }

                foreach (var sec in thumbSec)
                {
                    ThumbSec.Add(sec);
                }

                isThumbnail = true;
            }
        }
    }
}
