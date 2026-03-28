using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 複数フレームを1枚のシートへ並べる責務だけを切り出す。
    /// </summary>
    public static class ThumbnailSheetComposer
    {
        public static Bitmap ConcatImages(IReadOnlyList<string> paths, int columns, int rows)
        {
            // 行ごとに横結合してから、最後に縦結合して1枚へまとめる。
            List<Mat> rowResults = [];
            Mat all = new();
            try
            {
                for (int row = 0; row < rows; row++)
                {
                    List<Mat> sourceRow = [];
                    try
                    {
                        for (int column = 0; column < columns; column++)
                        {
                            sourceRow.Add(new Mat(paths[column + (row * columns)]));
                        }

                        Mat rowResult = new();
                        Cv2.HConcat(sourceRow, rowResult);
                        rowResults.Add(rowResult);
                    }
                    finally
                    {
                        foreach (Mat sourceMat in sourceRow)
                        {
                            sourceMat.Dispose();
                        }
                    }
                }

                Cv2.VConcat(rowResults, all);
                return BitmapConverter.ToBitmap(all);
            }
            finally
            {
                foreach (Mat rowResult in rowResults)
                {
                    rowResult.Dispose();
                }

                all.Dispose();
            }
        }
    }
}
