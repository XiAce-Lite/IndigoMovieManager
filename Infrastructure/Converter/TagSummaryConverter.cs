using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace IndigoMovieManager.Converter
{
    /// <summary>
    /// タグ一覧を軽い 1 行サマリへ落として、非選択行の Visual 負荷を抑える。
    /// </summary>
    public sealed class TagSummaryConverter : IValueConverter
    {
        private const int DefaultMaxTagCount = 4;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text)
            {
                return text?.Trim() ?? "";
            }

            if (value is not IEnumerable enumerable)
            {
                return "";
            }

            int maxTagCount = ResolveMaxTagCount(parameter);
            List<string> tags = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            foreach (object item in enumerable)
            {
                string tag = item?.ToString()?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(tag) || !seen.Add(tag))
                {
                    continue;
                }

                tags.Add(tag);
            }

            if (tags.Count < 1)
            {
                return "";
            }

            int visibleCount = Math.Min(tags.Count, maxTagCount);
            string summary = string.Join("  ", tags.Take(visibleCount));
            if (tags.Count > visibleCount)
            {
                summary += $"  ... (+{tags.Count - visibleCount})";
            }

            return summary;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static int ResolveMaxTagCount(object parameter)
        {
            if (parameter is int intValue && intValue > 0)
            {
                return intValue;
            }

            if (
                parameter is string text
                && int.TryParse(text, out int parsed)
                && parsed > 0
            )
            {
                return parsed;
            }

            return DefaultMaxTagCount;
        }
    }
}
