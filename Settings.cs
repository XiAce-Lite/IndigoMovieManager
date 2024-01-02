using System.Data;
using System.Diagnostics;

namespace IndigoMovieManager
{
    public class Settings
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

        public Settings(string dbFullPath) {

            //systemテーブルのデータは全て文字列。attr(キー):value(値)の構成
            //必要なデータは、thum、bookmark、keepHistory、playerPrg、playerParam
            string keys = "'thum','bookmark','keepHistory','playerPrg','playerParam'";
            DataTable systemTable = SQLite.GetData(dbFullPath, $"select * from system where attr in ({keys})");

            //将来的な話だと、

            //checkExist = ファイルの存在チェックをするかどうか。0 or 1
            //incrementalSearch = インクリメンタルサーチをするかどうか。0 or 1
            //autoThum = サムネイルの自動作成。0 or 1
            //extAdd = 追加の拡張子
            //extDel = 除外の拡張子

            //辺りは追加してもいいかも知れないけども、なくてもいいんじゃね？とは思う。

            if (systemTable == null ) { return; }
            if (systemTable.Rows.Count == 0 ) { return; }

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

        public string ThumbFolder { get { return thumbFolder; } set {  thumbFolder = value; } }
        public string BookmarkFolder { get { return bookmarkFolder; } set {  bookmarkFolder = value; } }
        public string PlayerPrg { get {  return playerPrg; } set {  playerPrg = value; } }  
        public string PlayerParam { get {  return playerParam; } set { playerParam = value; } }
        public int KeepHistory { get { return keepHistory; } set {  keepHistory = value; } }

        public bool ThumbExists { get { return thumbExists; } set { thumbExists = value; } }
        public bool PlayerPrgExists { get { return playerPrgExists; } set { playerPrgExists = value; } }
        public bool BookmarkExists { get { return bookmarkExists; } set { bookmarkExists = value; } }
        public bool KeepHistoryExists { get { return keepHistoryExists; } set { keepHistoryExists = value; } }
        public bool PlayerParamExists { get { return playerParamExists; } set { playerParamExists = value; } }

    }
}
