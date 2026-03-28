using System;
using System.Globalization;
using System.Windows.Data;

namespace IndigoMovieManager.Converter
{
    /// <summary>
    /// Big 詳細の上段を非選択時だけ 1 行へ畳み、行ごとの Visual 数を減らす。
    /// </summary>
    public sealed class BigDetailSummaryConverter : IMultiValueConverter
    {
        private static readonly FileSizeConverter FileSizeConverter = new();

        public object Convert(
            object[] values,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            string score = values.Length > 0 ? values[0]?.ToString() ?? "" : "";
            string size = ResolveSizeText(values.Length > 1 ? values[1] : null, culture);
            string length = values.Length > 2 ? values[2]?.ToString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(score) && string.IsNullOrWhiteSpace(size))
            {
                return length;
            }

            // 5x2 と Big の幅を詰めやすいように、要点は行ごとに返す。
            string[] lines =
            [
                $"S:{score}",
                size,
                length,
            ];

            return string.Join(
                Environment.NewLine,
                Array.FindAll(lines, static line => !string.IsNullOrWhiteSpace(line))
            );
        }

        public object[] ConvertBack(
            object value,
            Type[] targetTypes,
            object parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException();
        }

        private static string ResolveSizeText(object value, CultureInfo culture)
        {
            if (value is long longValue)
            {
                return FileSizeConverter.Convert(value, typeof(string), null, culture)?.ToString() ?? "";
            }

            if (value is int intValue)
            {
                return FileSizeConverter.Convert((long)intValue, typeof(string), null, culture)?.ToString() ?? "";
            }

            if (
                value is string text
                && long.TryParse(text, NumberStyles.Integer, culture, out long parsed)
            )
            {
                return FileSizeConverter.Convert(parsed, typeof(string), null, culture)?.ToString() ?? "";
            }

            return "";
        }
    }
}
