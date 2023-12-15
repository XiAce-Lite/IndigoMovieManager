using System.Globalization;
using System.Windows.Data;

namespace IndigoMovieManager.Converter
{
    /// <summary>
    /// バイトでサイズを持ってるので、見易い形に。パクリ元 https://qiita.com/paralleltree/items/449a96debdade0adb377
    /// </summary>
    public class FileSizeConverter : IValueConverter
    {
        static readonly string[] Suffix = ["", "K", "M", "G", "T"];
        static readonly double Unit = 1024;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not long) throw new ArgumentException("long型じゃないよ。", nameof(value));
            double size = (long)value * Unit;

            int i;
            for (i = 0; i < Suffix.Length - 1; i++)
            {
                if (size < Unit) break;
                size /= Unit;
            }

            return string.Format("{0:" + (i == 0 ? "0" : "0.0") + "} {1}B", size, Suffix[i]);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
