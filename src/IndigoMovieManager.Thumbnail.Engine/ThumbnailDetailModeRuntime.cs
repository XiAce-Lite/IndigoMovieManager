using System;

namespace IndigoMovieManager.Thumbnail
{
    // 詳細タブの表示モードは UI / worker / engine で同じ値を共有する。
    public static class ThumbnailDetailModeRuntime
    {
        public const string Standard = "Standard";
        public const string WhiteBrowserCompatible = "WhiteBrowserCompatible";
        public const string EnvironmentVariableName = "INDIGO_DETAIL_THUMB_MODE";

        public static string Normalize(string mode)
        {
            string normalizedMode = mode?.Trim() ?? "";
            if (
                string.Equals(
                    normalizedMode,
                    Standard,
                    StringComparison.OrdinalIgnoreCase
                )
            )
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
            return Normalize(mode) == Standard
                ? ThumbnailLayoutProfileResolver.DetailStandard
                : ThumbnailLayoutProfileResolver.DetailWhiteBrowserCompatible;
        }
    }
}
