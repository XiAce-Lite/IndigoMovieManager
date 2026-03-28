using System;

namespace IndigoMovieManager.Thumbnail
{
    // 詳細タブの表示モードは UI / worker / engine で同じ値を共有する。
    public static class ThumbnailDetailModeRuntime
    {
        public const string Grid = "Grid";
        public const string Small = "Small";
        public const string Big = "Big";
        public const string List = "List";
        public const string FiveByTwo = "FiveByTwo";
        public const string WhiteBrowser = "WhiteBrowser";
        private const string LegacyStandard = "Standard";
        private const string LegacyBig10 = "Big10";
        private const string LegacyWhiteBrowserCompatible = "WhiteBrowserCompatible";
        public const string EnvironmentVariableName = "INDIGO_DETAIL_THUMB_MODE";

        public static string Normalize(string mode)
        {
            string normalizedMode = mode?.Trim() ?? "";
            if (string.Equals(normalizedMode, Grid, StringComparison.OrdinalIgnoreCase))
            {
                return Grid;
            }

            if (string.Equals(normalizedMode, Small, StringComparison.OrdinalIgnoreCase))
            {
                return Small;
            }

            if (string.Equals(normalizedMode, Big, StringComparison.OrdinalIgnoreCase))
            {
                return Big;
            }

            if (string.Equals(normalizedMode, List, StringComparison.OrdinalIgnoreCase))
            {
                return List;
            }

            if (string.Equals(normalizedMode, FiveByTwo, StringComparison.OrdinalIgnoreCase))
            {
                return FiveByTwo;
            }

            if (string.Equals(normalizedMode, WhiteBrowser, StringComparison.OrdinalIgnoreCase))
            {
                return WhiteBrowser;
            }

            // 旧設定値はここで吸収し、UI 選択肢は現在の6種へ寄せる。
            if (string.Equals(normalizedMode, LegacyStandard, StringComparison.OrdinalIgnoreCase))
            {
                return Grid;
            }

            if (string.Equals(normalizedMode, LegacyWhiteBrowserCompatible, StringComparison.OrdinalIgnoreCase))
            {
                return WhiteBrowser;
            }

            if (string.Equals(normalizedMode, LegacyBig10, StringComparison.OrdinalIgnoreCase))
            {
                return FiveByTwo;
            }

            return FiveByTwo;
        }

        public static string ReadRuntimeMode()
        {
            return Normalize(Environment.GetEnvironmentVariable(EnvironmentVariableName));
        }

        public static void ApplyToProcess(string mode)
        {
            Environment.SetEnvironmentVariable(EnvironmentVariableName, Normalize(mode));
        }

        public static int GetDisplayWidth(string mode)
        {
            ThumbnailLayoutProfile layout = ResolveLayout(mode);
            // 詳細タブは 1コマの大きさではなく、実際に表示する合成画像全体の幅で見せる。
            return layout.Width * layout.Columns;
        }

        public static int GetDisplayHeight(string mode)
        {
            ThumbnailLayoutProfile layout = ResolveLayout(mode);
            // 行数を持つレイアウトは、縦も合成後サイズへ合わせる。
            return layout.Height * layout.Rows;
        }

        public static ThumbnailLayoutProfile ResolveLayout(string mode)
        {
            return Normalize(mode) switch
            {
                Grid => ThumbnailLayoutProfileResolver.Grid,
                Small => ThumbnailLayoutProfileResolver.Small,
                Big => ThumbnailLayoutProfileResolver.Big,
                List => ThumbnailLayoutProfileResolver.List,
                FiveByTwo => ThumbnailLayoutProfileResolver.Big10,
                WhiteBrowser => ThumbnailLayoutProfileResolver.DetailWhiteBrowser,
                _ => ThumbnailLayoutProfileResolver.Big10,
            };
        }
    }
}
