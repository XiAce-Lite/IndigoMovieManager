namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 既知の非動画シグネチャを、先頭数バイトだけで軽く判定する。
    /// </summary>
    internal static class ThumbnailFileSignatureInspector
    {
        private static readonly byte[] AppleDoubleMagic = [0x00, 0x05, 0x16, 0x07];

        public static bool IsAppleDouble(string moviePath)
        {
            return HasHeader(moviePath, AppleDoubleMagic);
        }

        public static bool IsShockwaveFlash(string moviePath)
        {
            if (!TryReadHeader(moviePath, 3, out byte[] header))
            {
                return false;
            }

            return header[1] == 0x57
                && header[2] == 0x53
                && (header[0] == 0x46 || header[0] == 0x43 || header[0] == 0x5A);
        }

        private static bool HasHeader(string moviePath, IReadOnlyList<byte> expectedHeader)
        {
            if (expectedHeader == null || expectedHeader.Count < 1)
            {
                return false;
            }

            if (!TryReadHeader(moviePath, expectedHeader.Count, out byte[] header))
            {
                return false;
            }

            for (int i = 0; i < expectedHeader.Count; i++)
            {
                if (header[i] != expectedHeader[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryReadHeader(string moviePath, int length, out byte[] header)
        {
            header = [];
            if (string.IsNullOrWhiteSpace(moviePath) || length < 1 || !File.Exists(moviePath))
            {
                return false;
            }

            try
            {
                using FileStream stream = new(
                    moviePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete
                );
                if (stream.Length < length)
                {
                    return false;
                }

                header = new byte[length];
                int read = stream.Read(header, 0, length);
                return read == length;
            }
            catch
            {
                header = [];
                return false;
            }
        }
    }
}
