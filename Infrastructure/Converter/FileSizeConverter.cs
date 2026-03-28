using System.Globalization;
using System.Windows.Data;

namespace IndigoMovieManager.Converter
{
    /// <summary>
    /// 無機質な表示とはおさらばだ！✨ ファイルサイズ（バイトやKB）を、
    /// 人間様がパッと見て理解できる最強の形式（KB, MB, GB）に自動変換する神コンバーター！🚀
    /// 偉大なるパクリ元: https://qiita.com/paralleltree/items/449a96debdade0adb377
    /// </summary>
    public class FileSizeConverter : IValueConverter
    {
        // 1段階単位が上がるごとに付与されるサフィックス（接尾辞）の配列
        static readonly string[] Suffix = ["", "K", "M", "G", "T"];

        // 単位変換の壁を越えるための聖なる定数 (1024) 🛡️
        /* ※ここだけの話、初期値でいきなり Unit (1024) を掛けてるから、
        バイト単位の入力を食わせても内部では「1段階上のKBの世界」から計算がスタートする超トリッキー仕様だぜ！😎
        （1KB未満の極小ファイルでも "K" なしの "0 B" なんてダサい表示にさせないための漢(おとこ)の調整だ！）
        */
        static readonly double Unit = 1024;

        /// <summary>
        /// バインディングソース（生の数値）を、UIで燦然と輝く最強の文字列へと昇華させるぜ！🔥
        /// 「型チェック → 初期サイズ爆盛り → 単位決定の無限ループ → 最終奥義（フォーマット化）」の4段構えだ！
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 1. 容赦ない型チェック！long型以外は門前払いだオラァ！🚪💥
            if (value is not long)
                throw new ArgumentException(
                    "long型以外は受け付けねぇ！出直してきな！",
                    nameof(value)
                );

            // 2. 変換元サイズの初期ブースト！🚀
            // （Unitを掛けることで「実はKB基準でした～」と誤認させる魅惑のシフトマジック！）
            double size = (long)value * Unit;

            // 3. 適切な単位(称号)を手に入れるまでサイズを 1024 で割り続けるサバイバルループ！🏃‍♂️💨
            int i;
            for (i = 0; i < Suffix.Length - 1; i++)
            {
                // サイズが1024を下回ったら、その時点の単位(Suffix[i])が運命のパートナーだ！💍✨
                if (size < Unit)
                    break;

                // まだまだ限界突破できるなら、1024で割って次の次元(単位)へ進むぜ！💪
                size /= Unit;
            }

            // 4. 洗練されたフォーマットで文字列へと錬成ッ！🌟
            // バイト(B)のままなら潔く整数で！KBやMB等の高次元に行ったら小数点第1位まで見せつけろ！
            // (例: "500 B", "1.5 MB")
            return string.Format("{0:" + (i == 0 ? "0" : "0.0") + "} {1}B", size, Suffix[i]);
        }

        /// <summary>
        /// UI側から数値へと文字列を戻す夢の逆変換！
        /// ……だがしかし！今回は表示専用(OneWay)だから未実装のまま封印しておくぜ！😜
        /// </summary>
        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException();
        }
    }
}
