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
            // facade 側は仕様データを作るだけにし、WB互換バイナリ化は serializer へ寄せる。
            WhiteBrowserThumbInfoSerializer.CreateBuffers(
                ToSheetSpec(),
                out byte[] secBuffer,
                out byte[] infoBuffer
            );
            SecBuffer = secBuffer;
            InfoBuffer = infoBuffer;
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
            if (!WhiteBrowserThumbInfoSerializer.TryReadFromJpeg(fileName, out ThumbnailSheetSpec spec))
            {
                return;
            }
            ApplySheetSpec(spec);
        }

        public ThumbnailSheetSpec ToSheetSpec()
        {
            return ThumbnailSheetSpec.FromThumbInfo(this);
        }

        public void ApplySheetSpec(ThumbnailSheetSpec spec)
        {
            if (spec == null)
            {
                return;
            }

            thumbCounts = spec.ThumbCount;
            thumbWidth = spec.ThumbWidth;
            thumbHeight = spec.ThumbHeight;
            thumbColumns = spec.ThumbColumns;
            thumbRows = spec.ThumbRows;
            thumbSec = spec.CaptureSeconds != null ? [.. spec.CaptureSeconds] : [];
            isThumbnail = true;
            WhiteBrowserThumbInfoSerializer.CreateBuffers(
                spec,
                out byte[] secBuffer,
                out byte[] infoBuffer
            );
            SecBuffer = secBuffer;
            InfoBuffer = infoBuffer;
        }

        public static ThumbInfo FromSheetSpec(ThumbnailSheetSpec spec)
        {
            ThumbInfo thumbInfo = new();
            thumbInfo.ApplySheetSpec(spec);
            return thumbInfo;
        }
    }
}
