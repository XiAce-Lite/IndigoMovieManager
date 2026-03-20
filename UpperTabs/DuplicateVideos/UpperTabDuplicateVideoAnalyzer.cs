using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace IndigoMovieManager.UpperTabs.DuplicateVideos
{
    internal static partial class UpperTabDuplicateVideoAnalyzer
    {
        // `prob-3` 系だけを初期版の補助情報として拾う。
        [GeneratedRegex(@"(?i)(prob-?\d+)")]
        private static partial Regex ProbRegex();

        // hash単位にまとめ、左ペイン用の代表行を決める。
        internal static UpperTabDuplicateGroupSummary[] BuildGroupSummaries(
            IEnumerable<UpperTabDuplicateMovieRecord> records
        )
        {
            if (records == null)
            {
                return [];
            }

            List<UpperTabDuplicateGroupSummary> result = [];
            foreach (
                IGrouping<string, UpperTabDuplicateMovieRecord> group in records
                    .Where(x => !string.IsNullOrWhiteSpace(x.Hash))
                    .GroupBy(x => x.Hash, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(x => x.Count())
                    .ThenByDescending(x => x.Max(item => item.MovieSize))
            )
            {
                UpperTabDuplicateMovieRecord representative = group
                    .OrderByDescending(x => x.MovieSize)
                    .ThenByDescending(x => x.FileDateText, StringComparer.Ordinal)
                    .ThenByDescending(x => x.MovieId)
                    .First();

                long maxSize = group.Max(x => x.MovieSize);
                long minSize = group.Min(x => x.MovieSize);
                result.Add(
                    new UpperTabDuplicateGroupSummary(
                        representative.Hash,
                        representative,
                        group.Count(),
                        maxSize,
                        minSize
                    )
                );
            }

            return [.. result];
        }

        // 右ペインではサイズ差をすぐ見たいので、先頭との比較だけ短く返す。
        internal static string BuildSizeCompareText(long currentSize, long maxSize, long minSize)
        {
            if (maxSize <= 0)
            {
                return "-";
            }

            if (currentSize >= maxSize)
            {
                return "最大";
            }

            if (currentSize <= minSize)
            {
                return "最小";
            }

            long diff = maxSize - currentSize;
            return $"-{FormatKilobytesAsMegabytes(diff)}";
        }

        internal static string ExtractProbText(string movieName, string moviePath)
        {
            string source = string.IsNullOrWhiteSpace(movieName)
                ? Path.GetFileName(moviePath ?? "")
                : movieName;
            if (string.IsNullOrWhiteSpace(source))
            {
                return "";
            }

            Match match = ProbRegex().Match(source);
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : "";
        }

        internal static string BuildDisplayMovieName(string movieName, string moviePath)
        {
            string extension = Path.GetExtension(moviePath ?? "");
            if (string.IsNullOrWhiteSpace(movieName))
            {
                return Path.GetFileName(moviePath ?? "");
            }

            if (
                !string.IsNullOrWhiteSpace(extension)
                && !movieName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            )
            {
                return movieName + extension;
            }

            return movieName;
        }

        private static string FormatKilobytesAsMegabytes(long sizeInKilobytes)
        {
            if (sizeInKilobytes <= 0)
            {
                return "0 MB";
            }

            double sizeInMegabytes = sizeInKilobytes / 1024d;
            return $"{sizeInMegabytes:0.0} MB";
        }
    }

    internal readonly record struct UpperTabDuplicateGroupSummary(
        string Hash,
        UpperTabDuplicateMovieRecord Representative,
        int DuplicateCount,
        long MaxMovieSize,
        long MinMovieSize
    );
}
