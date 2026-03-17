using System;
using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    internal static class ThumbnailErrorPlaceholderHelper
    {
        private static readonly string[] PlaceholderFileNames =
        [
            "errorSmall.jpg",
            "errorBig.jpg",
            "errorGrid.jpg",
            "errorList.jpg",
        ];

        // 組み込みの error 代替画像だけを拾い、通常jpgや ERROR マーカーとは分離する。
        internal static bool IsPlaceholderPath(string thumbPath)
        {
            if (string.IsNullOrWhiteSpace(thumbPath))
            {
                return false;
            }

            string fileName = Path.GetFileName(thumbPath.Trim());
            for (int i = 0; i < PlaceholderFileNames.Length; i++)
            {
                if (
                    string.Equals(
                        fileName,
                        PlaceholderFileNames[i],
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        // 一覧ソートでは、今 UI が持っている placeholder 数をそのままエラー量として使う。
        internal static int CountPlaceholders(global::IndigoMovieManager.MovieRecords movie)
        {
            if (movie == null)
            {
                return 0;
            }

            int count = 0;
            if (IsPlaceholderPath(movie.ThumbPathSmall))
            {
                count++;
            }

            if (IsPlaceholderPath(movie.ThumbPathBig))
            {
                count++;
            }

            if (IsPlaceholderPath(movie.ThumbPathGrid))
            {
                count++;
            }

            if (IsPlaceholderPath(movie.ThumbPathList))
            {
                count++;
            }

            if (IsPlaceholderPath(movie.ThumbPathBig10))
            {
                count++;
            }

            if (IsPlaceholderPath(movie.ThumbDetail))
            {
                count++;
            }

            return count;
        }
    }
}
