using System;

namespace IndigoMovieManager.Thumbnail
{
    // 詳細タブの表示モードは UI / worker / engine で同じ値を共有する。
    public static class ThumbnailDetailModeRuntime
    {
        public const string Small = "Small";
        public const string Big = "Big";
        public const string Grid = "Grid";
        public const string List = "List";
        public const string Big10 = "Big10";
        public const string Standard = "Standard";
        public const string WhiteBrowserCompatible = "WhiteBrowserCompatible";
        public const string EnvironmentVariableName = "INDIGO_DETAIL_THUMB_MODE";

        public static string Normalize(string mode)
        {
            string normalizedMode = mode?.Trim() ?? "";
            if (string.Equals(normalizedMode, Small, StringComparison.OrdinalIgnoreCase))
            {
                return Small;
            }

            if (string.Equals(normalizedMode, Big, StringComparison.OrdinalIgnoreCase))
            {
                return Big;
            }

            if (string.Equals(normalizedMode, Grid, StringComparison.OrdinalIgnoreCase))
            {
                return Grid;
            }

            if (string.Equals(normalizedMode, List, StringComparison.OrdinalIgnoreCase))
            {
                return List;
            }

            if (string.Equals(normalizedMode, Big10, StringComparison.OrdinalIgnoreCase))
            {
                return Big10;
            }

            if (string.Equals(normalizedMode, Standard, StringComparison.OrdinalIgnoreCase))
            {
                return Standard;
            }

            return WhiteBrowserCompatible;
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
            return ResolveLayout(mode).Width;
        }

        public static int GetDisplayHeight(string mode)
        {
            return ResolveLayout(mode).Height;
        }

        public static ThumbnailLayoutProfile ResolveLayout(string mode)
        {
            return Normalize(mode) switch
            {
                Small => ThumbnailLayoutProfileResolver.Small,
                Big => ThumbnailLayoutProfileResolver.Big,
                Grid => ThumbnailLayoutProfileResolver.Grid,
                List => ThumbnailLayoutProfileResolver.List,
                Big10 => ThumbnailLayoutProfileResolver.Big10,
                Standard => ThumbnailLayoutProfileResolver.DetailStandard,
                _ => ThumbnailLayoutProfileResolver.DetailWhiteBrowserCompatible,
            };
        }
    }
}
