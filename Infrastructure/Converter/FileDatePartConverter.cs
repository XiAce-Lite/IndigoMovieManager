using System;
using System.Globalization;
using System.Windows.Data;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager.Converter
{
    /// <summary>
    /// 一覧表示で使う日付文字列を日付部と時刻部へ分ける。
    /// </summary>
    public sealed class FileDatePartConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string mode = parameter?.ToString() ?? "Date";
            string text = value?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            if (string.Equals(mode, "Duration", StringComparison.OrdinalIgnoreCase))
            {
                if (TimeSpan.TryParse(text, culture, out TimeSpan duration))
                {
                    // 一覧の可読性を優先して区切りの前後に空白を入れる。
                    return $"{(int)duration.TotalHours:00} : {duration.Minutes:00} : {duration.Seconds:00}";
                }

                string[] durationParts = text.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (durationParts.Length == 3)
                {
                    return $"{durationParts[0].Trim()} : {durationParts[1].Trim()} : {durationParts[2].Trim()}";
                }

                return text;
            }

            if (TryParseDbDateTimeText(text, out DateTime parsed))
            {
                // 流れを固定して、一覧上の見え方を安定させる。
                return string.Equals(mode, "Time", StringComparison.OrdinalIgnoreCase)
                    ? parsed.ToString("HH：mm：ss", culture)
                    : parsed.ToString("yyyy-MM-dd", culture);
            }

            string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return string.Equals(mode, "Time", StringComparison.OrdinalIgnoreCase)
                    ? parts[1].Replace(":", "：", StringComparison.Ordinal)
                    : parts[0];
            }

            return string.Equals(mode, "Time", StringComparison.OrdinalIgnoreCase)
                ? ""
                : text;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
