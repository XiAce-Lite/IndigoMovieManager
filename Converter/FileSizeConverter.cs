using System.Globalization;
using System.Windows.Data;

namespace IndigoMovieManager.Converter
{
    /// <summary>
    /// ファイルサイズ（バイトまたはキロバイト単位）を、人間が読みやすい形式（KB, MB, GBなど）の文字列に変換するコンバーター。
    /// パクリ元: https://qiita.com/paralleltree/items/449a96debdade0adb377
    /// </summary>
    public class FileSizeConverter : IValueConverter
    {
        // 1段階単位が上がるごとに付与されるサフィックス（接尾辞）の配列
        static readonly string[] Suffix = ["", "K", "M", "G", "T"];

        // 単位変換の閾値および除数となる基準値 (1024)
        /*※ちなみに、初期値に Unit (1024) を掛けているため、渡される値がバイト単位だとするならば、
        実質的には1段階上のキロバイト相当として処理される少々トリッキーな仕様になっています。
        (1KB未満でも "K" のつかない "0 B" などに落とし込むための調整の可能性があります)
        */
        static readonly double Unit = 1024;

        /// <summary>
        /// バインディングソース（数値）からターゲット（UI表示用の文字列）へフォーマット変換を行う
        /// 「型チェック → 初期サイズ算出 → 単位決定ループ → 最終的な文字列フォーマット」
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 1. 入力値の型チェック。long型以外がバインドされた場合は例外とする
            if (value is not long)
                throw new ArgumentException("long型じゃないよ。", nameof(value));

            // 2. 変換元サイズの初期設定
            // ※注意: 現在のロジックでは Unit(1024) を乗算しているため、
            // 入力されたバイト値を実質的に「キロバイト基準」として1段階シフトさせる動きになります。
            // (1KB未満でも "K" のつかない "0 B" などに落とし込むための調整の可能性があります)
            double size = (long)value * Unit;

            // 3. 適切な単位が見つかるまでサイズを 1024 で割り続けるループ処理
            int i;
            for (i = 0; i < Suffix.Length - 1; i++)
            {
                // 現在のサイズが1024未満になったら、その時点の単位(Suffix[i])がふさわしいと判断してループを抜ける
                if (size < Unit)
                    break;

                // 1024以上であれば、さらに上の単位にするために 1024 で割り、インデックス i を進める
                size /= Unit;
            }

            // 4. 決定した単位を用いて数値をフォーマットし、文字列として返す
            // B(バイト)のまま(i==0)なら小数点以下を表示せず(例: "500 B")、
            // K, M, G 等の単位(i>0)がついた場合は小数点第1位まで表示する(例: "1.5 MB")
            return string.Format("{0:" + (i == 0 ? "0" : "0.0") + "} {1}B", size, Suffix[i]);
        }

        /// <summary>
        /// ターゲット（UI上の文字列）からバインディングソースへの逆変換。
        /// このコンバーターはOneWay（表示専用）を想定しているため未実装のままとしておく。
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
