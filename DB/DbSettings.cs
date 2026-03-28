using System.Data;
using System.Diagnostics;

namespace IndigoMovieManager.DB
{
    /// <summary>
    /// DBの「system」テーブルに眠る重要設定（サムネ保管庫、外部プレイヤー召喚呪文など）を叩き起こし、
    /// アプリケーション全体へ轟かせるための最強共有クラスだ！🌍✨
    /// （※超絶注意: プロパティがstaticだから、インスタンスを作った瞬間に全宇宙の設定が上書きされる諸刃の剣だぜ！⚔️）
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
        /// 伝説の始まり（コンストラクタ）！生成された瞬間にDBから設定値をブッコ抜き、
        /// 内部のstatic変数という名の神棚へ奉納するぜ！🙏🔥
        /// </summary>
        /// <param name="dbFullPath">魂の器たるSQLite DBファイルパス</param>
        public DbSettings(string dbFullPath)
        {
            // 1. 狙うべき宝の地図（キー項目）を定義！🗺️
            // systemテーブルのデータは全て文字列。attr(キー):value(値)の構成だ！
            // 必要なデータは、thum、bookmark、keepHistory、playerPrg、playerParam の5つ！
            string keys = "'thum','bookmark','keepHistory','playerPrg','playerParam'";

            // 2. DBから対象キーの設定値をDataTableの網で一網打尽にするぜ！🎣
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

            // データが存在しなければ大人しく早期リターンだ！（何もないなら帰る！）💨
            if (systemTable == null)
            {
                return;
            }
            if (systemTable.Rows.Count == 0)
            {
                return;
            }

            // 3. 取得した各Row(行)をガンガンループで回し、キー(attr)に応じてstatic変数へ叩き込む！💥
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

        /// <summary>
        /// 全てのサムネが還る魂の故郷！サムネイル保存フォルダ！🖼️✨
        /// </summary>
        public string ThumbFolder
        {
            get { return thumbFolder; }
            set { thumbFolder = value; }
        }

        /// <summary>
        /// ブックマーク画像たちの秘密基地！保存フォルダだ！🔖💖
        /// </summary>
        public string BookmarkFolder
        {
            get { return bookmarkFolder; }
            set { bookmarkFolder = value; }
        }

        /// <summary>
        /// 動画を再生するための伝説の剣（外部プレイヤーのフルパス）！⚔️
        /// </summary>
        public string PlayerPrg
        {
            get { return playerPrg; }
            set { playerPrg = value; }
        }

        /// <summary>
        /// 伝説の剣に込める必殺のルーン（外部プレイヤー起動時の引数パラメータ）！🔥
        /// </summary>
        public string PlayerParam
        {
            get { return playerParam; }
            set { playerParam = value; }
        }

        /// <summary>
        /// 過去の栄光をどこまで刻むか！？履歴の保持件数！📜
        /// </summary>
        public int KeepHistory
        {
            get { return keepHistory; }
            set { keepHistory = value; }
        }

        #endregion

        #region 設定値の存在有無判定プロパティ

        /// <summary>
        /// サムネフォルダの設定はDBに存在するのか！？（存在フラグ）🚩
        /// </summary>
        public bool ThumbExists
        {
            get { return thumbExists; }
            set { thumbExists = value; }
        }

        /// <summary>
        /// 外部プレイヤーの設定はDBに存在するのか！？（存在フラグ）🚩
        /// </summary>
        public bool PlayerPrgExists
        {
            get { return playerPrgExists; }
            set { playerPrgExists = value; }
        }

        /// <summary>
        /// ブックマークフォルダの設定はDBに存在するのか！？（存在フラグ）🚩
        /// </summary>
        public bool BookmarkExists
        {
            get { return bookmarkExists; }
            set { bookmarkExists = value; }
        }

        /// <summary>
        /// 履歴件数の設定はDBに存在するのか！？（存在フラグ）🚩
        /// </summary>
        public bool KeepHistoryExists
        {
            get { return keepHistoryExists; }
            set { keepHistoryExists = value; }
        }

        /// <summary>
        /// 外部プレイヤー起動引数の設定はDBに存在するのか！？（存在フラグ）🚩
        /// </summary>
        public bool PlayerParamExists
        {
            get { return playerParamExists; }
            set { playerParamExists = value; }
        }

        #endregion
    }
}
