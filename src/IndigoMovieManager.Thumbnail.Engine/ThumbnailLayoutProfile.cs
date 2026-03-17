using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    // tabIndex から決まる表示サイズだけを独立させ、TabInfo から UI 文脈を剥がす足場にする。
    public sealed class ThumbnailLayoutProfile
    {
        public ThumbnailLayoutProfile(int width, int height, int columns, int rows)
        {
            Width = width;
            Height = height;
            Columns = columns;
            Rows = rows;
        }

        public int Width { get; }
        public int Height { get; }
        public int Columns { get; }
        public int Rows { get; }
        public int DivCount => Columns * Rows;
        public string FolderName => $"{Width}x{Height}x{Columns}x{Rows}";

        public string BuildOutPath(string thumbRoot)
        {
            return Path.Combine(thumbRoot ?? "", FolderName);
        }
    }

    // まずは既存 tabIndex 規約をここへ集約し、呼び出し側の段階移行をしやすくする。
    public static class ThumbnailLayoutProfileResolver
    {
        public static readonly ThumbnailLayoutProfile Small = new(120, 90, 3, 1);
        public static readonly ThumbnailLayoutProfile Big = new(200, 150, 3, 1);
        public static readonly ThumbnailLayoutProfile Grid = new(160, 120, 1, 1);
        public static readonly ThumbnailLayoutProfile List = new(56, 42, 5, 1);
        public static readonly ThumbnailLayoutProfile Big10 = new(120, 90, 5, 2);
        public static readonly ThumbnailLayoutProfile DetailStandard = new(160, 120, 1, 1);
        public static readonly ThumbnailLayoutProfile DetailWhiteBrowserCompatible =
            new(120, 90, 1, 1);

        public static ThumbnailLayoutProfile Resolve(int tabIndex, string detailMode = null)
        {
            return tabIndex switch
            {
                0 => Small,
                1 => Big,
                2 => Grid,
                3 => List,
                4 => Big10,
                99 => ThumbnailDetailModeRuntime.ResolveLayout(detailMode),
                _ => Small,
            };
        }
    }
}
