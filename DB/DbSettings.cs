using System.Data;
using System.Diagnostics;

namespace IndigoMovieManager.DB
{
    /// <summary>
    /// DBの「system」テーブルから各種システム設定値（サムネフォルダ、外部プレイヤー設定など）
    /// を読み込み、アプリケーション全体で共有するためのクラス。
    /// （※注意: プロパティがstaticなため、インスタンス生成時に全体へ設定が上書き反映されます）
    /// </summary>
    public class DbSettings
    {
        private static string thumbFolder = "";
        private static string bookmarkFolder = "";
        private static int keepHistory = 0;
        private static string playerPrg = "";
        private static string playerParam = "";
        private static bool thumbExists = false;
        private static bool bookmarkExists = false;
        private static bool keepHistoryExists = false;
        private static bool playerPrgExists = false;
        private static bool playerParamExists = false;

        /// <summary>
        /// コンストラクタ。インスタンス生成時にDBから設定値をロードして内部のstatic変数に格納する。
        /// </summary>
        /// <param name="dbFullPath">読み込み対象のSQLite DBファイルパス</param>
        public DbSettings(string dbFullPath)
        {
            // 1. 対象となるキー項目を定義
            // systemテーブルのデータは全て文字列。attr(キー):value(値)の構成
            // 必要なデータは、thum、bookmark、keepHistory、playerPrg、playerParam
            string keys = "'thum','bookmark','keepHistory','playerPrg','playerParam'";

            // 2. DBから対象キーの設定値を一括でDataTableとして取得
            DataTable systemTable = SQLite.GetData(
                dbFullPath,
                $"select * from system where attr in ({keys})"
            );

            //将来的な話だと、

            //checkExist = ファイルの存在チェックをするかどうか。0 or 1
            //incrementalSearch = インクリメンタルサーチをするかどうか。0 or 1
            //autoThum = サムネイルの自動作成。0 or 1
            //extAdd = 追加の拡張子
            //extDel = 除外の拡張子

            //辺りは追加してもいいかも知れないけども、なくてもいいんじゃね？とは思う。

            // データが存在しなければ早期リターン
            if (systemTable == null)
            {
                return;
            }
            if (systemTable.Rows.Count == 0)
            {
                return;
            }

            // 3. 取得した各Row(行)をループし、キー(attr)に応じてstatic変数へ値を割り当てる
            foreach (DataRow row in systemTable.Rows)
            {
                switch (row[0])
                {
                    case "thum":
                        thumbFolder = row[1].ToString();
                        thumbExists = true;
                        break;
                    case "bookmark":
                        bookmarkFolder = row[1].ToString();
                        bookmarkExists = true;
                        break;
                    case "keepHistory":
                        // 履歴の保持件数は数値であるためキャストする
                        keepHistory = Convert.ToInt32(row[1].ToString());
                        keepHistoryExists = true;
                        break;
                    case "playerPrg":
                        playerPrg = row[1].ToString();
                        playerPrgExists = true;
                        break;
                    case "playerParam":
                        playerParam = row[1].ToString();
                        playerParamExists = true;
                        break;
                    default:
                        break;
                }
            }
        }

        #region 各種設定値プロパティ

        public string ThumbFolder
        {
            get { return thumbFolder; }
            set { thumbFolder = value; }
        }
        public string BookmarkFolder
        {
            get { return bookmarkFolder; }
            set { bookmarkFolder = value; }
        }
        public string PlayerPrg
        {
            get { return playerPrg; }
            set { playerPrg = value; }
        }
        public string PlayerParam
        {
            get { return playerParam; }
            set { playerParam = value; }
        }
        public int KeepHistory
        {
            get { return keepHistory; }
            set { keepHistory = value; }
        }

        #endregion

        #region 設定値の存在有無判定プロパティ

        public bool ThumbExists
        {
            get { return thumbExists; }
            set { thumbExists = value; }
        }
        public bool PlayerPrgExists
        {
            get { return playerPrgExists; }
            set { playerPrgExists = value; }
        }
        public bool BookmarkExists
        {
            get { return bookmarkExists; }
            set { bookmarkExists = value; }
        }
        public bool KeepHistoryExists
        {
            get { return keepHistoryExists; }
            set { keepHistoryExists = value; }
        }
        public bool PlayerParamExists
        {
            get { return playerParamExists; }
            set { playerParamExists = value; }
        }

        #endregion
    }
}
